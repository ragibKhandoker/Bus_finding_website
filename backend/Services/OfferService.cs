using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

public record OfferResult(int StatusCode, string Message, Offer? Offer = null, OfferTransaction? Entry = null, object? Extra = null)
{
    public bool Succeeded => StatusCode is >= 200 and < 300;
}

// A completed trip and how much of its fare offers have already covered.
public record TripOfferView(Guid TripId, string BusName, string Source, string Destination, DateTime? EndedAt, decimal Fare,
    decimal CouponSaved, decimal DiscountSaved)
{
    public decimal RemainingFare => Math.Max(0, Fare - CouponSaved - DiscountSaved);
}

// Offers: the welcome offer from Rewards settings, admin grants, cashback redemption into the wallet, and coupon or
// discount value taken off a completed trip's fare. Balances only ever change through a conditional UPDATE that
// checks the balance in the same statement (and holds the offer's row until the transaction ends), so two requests
// at once cannot spend the same taka twice. Each change is written to offer_transactions in the same transaction as
// the balance and any wallet credit, so either all of it is saved or none of it.
public class OfferService(AppDbContext db, WalletService wallets, NotificationService notifications)
{
    public const decimal MaxAmount = 1_000_000m;

    public RewardOptions Rewards => wallets.Rewards;

    // Adds a new user's offers with the configured welcome values to the context; the caller saves them together
    // with the user, so the welcome offer exists exactly when the account does.
    public Offer AddWelcomeOffers(User user, Wallet wallet)
    {
        var offer = new Offer
        {
            User = user,
            UserId = user.Id,
            Wallet = wallet,
            Cashback = Rewards.WelcomeCashback,
            Coupon = Rewards.WelcomeCoupon,
            Discount = Rewards.WelcomeDiscount,
        };
        db.Offers.Add(offer);
        foreach (var (kind, amount) in new[] { (OfferKinds.Cashback, offer.Cashback), (OfferKinds.Coupon, offer.Coupon), (OfferKinds.Discount, offer.Discount) })
        {
            if (amount > 0)
            {
                db.OfferTransactions.Add(new OfferTransaction
                {
                    Offer = offer, UserId = user.Id, Kind = kind, Amount = amount, BalanceAfter = amount,
                    Reason = OfferEntryReasons.Welcome, Description = $"Welcome {kind}", Reference = $"welcome:{kind}",
                });
            }
        }
        if (offer.Cashback + offer.Coupon + offer.Discount > 0)
        {
            notifications.Notify(user.Id, "Welcome offers", "Your welcome cashback, coupon and discount are ready on the Offers page.",
                NotificationTypes.Success, "/offers", NotificationCategories.Reward);
        }
        return offer;
    }

    // The user's offers. Accounts from before welcome offers were given at sign-up get theirs the first time; the
    // unique index on offers.user_id means two first visits at once still create one set.
    public async Task<Offer> EnsureAsync(Guid userId)
    {
        var offer = await db.Offers.Include(o => o.Wallet).FirstOrDefaultAsync(o => o.UserId == userId);
        if (offer is not null)
        {
            return offer;
        }
        var user = await db.Users.FirstAsync(u => u.Id == userId);
        var wallet = await wallets.GetWalletAsync(userId);
        AddWelcomeOffers(user, wallet);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUnique(ex))
        {
            db.ChangeTracker.Clear();
        }
        return await db.Offers.Include(o => o.Wallet).FirstAsync(o => o.UserId == userId);
    }

    public Task<List<OfferTransaction>> HistoryAsync(Guid userId, int take = 30) => db.OfferTransactions
        .Where(x => x.UserId == userId)
        .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
        .Take(take)
        .AsNoTracking()
        .ToListAsync();

    // The user's most recent completed trips with a fare, and what offers have covered on each.
    public async Task<List<TripOfferView>> TripsAsync(Guid userId, int take = 20)
    {
        var trips = await db.Trips
            .Where(t => t.UserId == userId && t.Status == TripStatus.Completed && t.Fare > 0)
            .OrderByDescending(t => t.EndedAt ?? t.CreatedAt)
            .Take(take)
            .Select(t => new { t.Id, t.BusName, t.Source, t.Destination, t.EndedAt, t.Fare })
            .AsNoTracking()
            .ToListAsync();
        var ids = trips.Select(t => t.Id).ToList();
        var saved = await db.OfferTransactions
            .Where(x => x.TripId != null && ids.Contains(x.TripId.Value))
            .Select(x => new { TripId = x.TripId!.Value, x.Kind, x.Amount })
            .ToListAsync();
        decimal Saved(Guid id, string kind) => -saved.Where(s => s.TripId == id && s.Kind == kind).Sum(s => s.Amount);
        return trips.Select(t => new TripOfferView(t.Id, t.BusName, t.Source, t.Destination, t.EndedAt, t.Fare,
            Saved(t.Id, OfferKinds.Coupon), Saved(t.Id, OfferKinds.Discount))).ToList();
    }

    private static string? AmountProblem(decimal amount) =>
        amount <= 0 ? "Amount must be greater than zero."
        : amount > MaxAmount ? "That amount is too large."
        : decimal.Round(amount, 2) != amount ? "Amounts can have at most 2 decimal places (poisha)."
        : null;

    private static bool IsUnique(DbUpdateException ex, string? constraint = null) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && (constraint is null || pg.ConstraintName == constraint);

    // Moves one balance by delta, but only while the result stays at or above zero (and, for spending, only while
    // the offers are active). Returns the new balance, or null when the balance was too low.
    private async Task<decimal?> ChangeAsync(Guid userId, string kind, decimal delta, bool spending, DateTime now)
    {
        var offers = db.Offers.Where(o => o.UserId == userId && (!spending || o.IsActive));
        var changed = kind switch
        {
            OfferKinds.Cashback => await offers.Where(o => o.Cashback + delta >= 0)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Cashback, o => o.Cashback + delta).SetProperty(o => o.UpdatedAt, now)),
            OfferKinds.Coupon => await offers.Where(o => o.Coupon + delta >= 0)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Coupon, o => o.Coupon + delta).SetProperty(o => o.UpdatedAt, now)),
            _ => await offers.Where(o => o.Discount + delta >= 0)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Discount, o => o.Discount + delta).SetProperty(o => o.UpdatedAt, now)),
        };
        if (changed == 0)
        {
            return null;
        }
        return await db.Offers.AsNoTracking().Where(o => o.UserId == userId)
            .Select(o => kind == OfferKinds.Cashback ? o.Cashback : kind == OfferKinds.Coupon ? o.Coupon : o.Discount)
            .SingleAsync();
    }

    private static decimal BalanceOf(Offer offer, string kind) =>
        kind == OfferKinds.Cashback ? offer.Cashback : kind == OfferKinds.Coupon ? offer.Coupon : offer.Discount;

    // A request repeated with the same reference returns what the first one did instead of running again.
    private async Task<OfferResult?> RepeatAsync(Guid userId, string? reference)
    {
        if (reference is null)
        {
            return null;
        }
        var earlier = await db.OfferTransactions.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId && x.Reference == reference);
        return earlier is null ? null : new(StatusCodes.Status200OK, "This request was already processed.", await EnsureAsync(userId), earlier);
    }

    private static string? CleanReference(string? reference) => string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();

    // Runs one offer change in a transaction. A repeated reference, or a duplicate trip use caught by the database,
    // rolls everything back and is answered without changing anything.
    private Task<OfferResult> InTransaction(Guid userId, string? reference, Func<DateTime, Task<OfferResult>> work)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            if (await RepeatAsync(userId, reference) is { } repeat)
            {
                return repeat;
            }
            await using var transaction = await db.Database.BeginTransactionAsync();
            OfferResult result;
            try
            {
                result = await work(DateTime.UtcNow);
                if (!result.Succeeded)
                {
                    return result;
                }
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException ex) when (IsUnique(ex, "ix_offer_transactions_one_per_reference"))
            {
                await transaction.RollbackAsync();
                db.ChangeTracker.Clear();
                return await RepeatAsync(userId, reference) ?? new(StatusCodes.Status409Conflict, "This request was already processed.");
            }
            catch (DbUpdateException ex) when (IsUnique(ex, "ix_offer_transactions_one_kind_per_trip"))
            {
                await transaction.RollbackAsync();
                return new(StatusCodes.Status409Conflict, "That offer was already used on this trip.");
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync();
                return new(StatusCodes.Status409Conflict, "Your wallet changed at the same moment. Nothing was used; please try again.");
            }
            db.ChangeTracker.Clear();
            return result with { Offer = await db.Offers.Include(o => o.Wallet).AsNoTracking().FirstAsync(o => o.UserId == userId) };
        });
    }

    // Pays cashback into the wallet. amount null means all of it.
    public async Task<OfferResult> RedeemCashbackAsync(Guid userId, decimal? amount, string? reference)
    {
        reference = CleanReference(reference);
        var offer = await EnsureAsync(userId);
        var take = amount ?? offer.Cashback;
        if (AmountProblem(take) is { } problem)
        {
            return new(StatusCodes.Status400BadRequest, amount is null ? "You have no cashback to redeem." : problem);
        }
        return await InTransaction(userId, reference, async now =>
        {
            var left = await ChangeAsync(userId, OfferKinds.Cashback, -take, spending: true, now);
            if (left is null)
            {
                return new(StatusCodes.Status400BadRequest, "Not enough cashback available.");
            }
            var wallet = await db.Wallets.FirstAsync(w => w.UserId == userId);
            var credit = wallets.Credit(wallet, take, OfferKinds.Cashback, "Cashback redeemed");
            var entry = new OfferTransaction
            {
                OfferId = offer.Id, UserId = userId, Kind = OfferKinds.Cashback, Amount = -take, BalanceAfter = left.Value,
                Reason = OfferEntryReasons.Redeemed, Description = "Cashback paid into wallet", WalletTransaction = credit,
                Reference = reference, CreatedAt = now,
            };
            db.OfferTransactions.Add(entry);
            return new(StatusCodes.Status200OK, $"৳{take:0.##} cashback added to your wallet.", Entry: entry);
        });
    }

    // Takes coupon or discount value off a completed trip's fare (as the server recorded it) and pays the saving back
    // into the wallet, since the app does not charge fares itself. Each kind can be used once per trip, never for
    // more than the fare still uncovered, and never for more than the balance. amount null uses as much as possible.
    public async Task<OfferResult> ApplyToTripAsync(Guid userId, string kind, Guid tripId, decimal? amount, string? reference)
    {
        reference = CleanReference(reference);
        if (amount is { } requested && AmountProblem(requested) is { } problem)
        {
            return new(StatusCodes.Status400BadRequest, problem);
        }
        var offer = await EnsureAsync(userId);
        var trip = await db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripId && t.UserId == userId);
        if (trip is null)
        {
            return new(StatusCodes.Status404NotFound, "Trip not found.");
        }
        if (trip.Status != TripStatus.Completed || trip.Fare <= 0)
        {
            return new(StatusCodes.Status409Conflict, "Offers can only be used on a completed trip with a fare.");
        }
        if (!offer.IsActive)
        {
            return new(StatusCodes.Status409Conflict, "Your offers are not active.");
        }

        return await InTransaction(userId, reference, async now =>
        {
            if (await db.OfferTransactions.AnyAsync(x => x.TripId == tripId && x.Kind == kind))
            {
                return new(StatusCodes.Status409Conflict, $"Your {kind} was already used on this trip.");
            }
            var covered = -await db.OfferTransactions.Where(x => x.TripId == tripId).SumAsync(x => x.Amount);
            var uncovered = trip.Fare - covered;
            var balance = BalanceOf(await db.Offers.AsNoTracking().FirstAsync(o => o.UserId == userId), kind);
            if (uncovered <= 0)
            {
                return new(StatusCodes.Status409Conflict, "This trip's fare is already fully covered.");
            }
            if (balance <= 0)
            {
                return new(StatusCodes.Status409Conflict, $"You have no {kind} value left.");
            }
            var saving = Math.Min(Math.Min(balance, uncovered), amount ?? decimal.MaxValue);
            var left = await ChangeAsync(userId, kind, -saving, spending: true, now);
            if (left is null)
            {
                return new(StatusCodes.Status409Conflict, $"Your {kind} balance changed at the same moment. Please try again.");
            }
            var route = $"{trip.Source} to {trip.Destination}";
            var wallet = await db.Wallets.FirstAsync(w => w.UserId == userId);
            var credit = wallets.Credit(wallet, saving, kind, $"{(kind == OfferKinds.Coupon ? "Coupon" : "Discount")} on trip {route}");
            var entry = new OfferTransaction
            {
                OfferId = offer.Id, UserId = userId, Kind = kind, Amount = -saving, BalanceAfter = left.Value,
                Reason = OfferEntryReasons.Trip, Description = $"Used on {trip.BusName}: {route}", TripId = tripId,
                WalletTransaction = credit, Reference = reference, CreatedAt = now,
            };
            db.OfferTransactions.Add(entry);
            var finalFare = uncovered - saving;
            return new(StatusCodes.Status200OK, $"৳{saving:0.##} {kind} applied. You now pay ৳{finalFare:0.##} for this trip.", Entry: entry,
                Extra: new { tripId, fare = trip.Fare, saving, coveredBefore = covered, finalFare });
        });
    }

    // Admin: adds value to one balance. A repeated reference does nothing the second time.
    public async Task<OfferResult> GrantAsync(Guid adminId, Guid userId, string kind, decimal amount, string? reference)
    {
        reference = CleanReference(reference);
        if (AmountProblem(amount) is { } problem)
        {
            return new(StatusCodes.Status400BadRequest, problem);
        }
        if (!await db.Users.AnyAsync(u => u.Id == userId))
        {
            return new(StatusCodes.Status404NotFound, "User not found.");
        }
        var offer = await EnsureAsync(userId);
        return await InTransaction(userId, reference, async now =>
        {
            var balance = await ChangeAsync(userId, kind, amount, spending: false, now);
            db.OfferTransactions.Add(new OfferTransaction
            {
                OfferId = offer.Id, UserId = userId, Kind = kind, Amount = amount, BalanceAfter = balance!.Value,
                Reason = OfferEntryReasons.Granted, Description = $"{kind} added by an admin", Reference = reference,
                CreatedById = adminId, CreatedAt = now,
            });
            notifications.Notify(userId, "New offer", $"৳{amount:0.##} {kind} was added to your offers.", NotificationTypes.Success,
                "/offers", NotificationCategories.Reward);
            return new(StatusCodes.Status200OK, $"৳{amount:0.##} {kind} added.");
        });
    }

    // Admin: sets the balances and the active flag; each change is recorded as an adjustment.
    public async Task<OfferResult> SetAsync(Guid adminId, Guid userId, decimal cashback, decimal coupon, decimal discount, bool isActive)
    {
        foreach (var value in new[] { cashback, coupon, discount })
        {
            if (value < 0 || value > MaxAmount || decimal.Round(value, 2) != value)
            {
                return new(StatusCodes.Status400BadRequest, "Offer values must be between 0 and 1,000,000 taka with at most 2 decimal places.");
            }
        }
        if (!await db.Users.AnyAsync(u => u.Id == userId))
        {
            return new(StatusCodes.Status404NotFound, "User not found.");
        }
        await EnsureAsync(userId);
        return await InTransaction(userId, null, async now =>
        {
            var offer = await db.Offers.FirstAsync(o => o.UserId == userId);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM offers WHERE id = {offer.Id} FOR UPDATE");
            await db.Entry(offer).ReloadAsync();
            foreach (var (kind, target) in new[] { (OfferKinds.Cashback, cashback), (OfferKinds.Coupon, coupon), (OfferKinds.Discount, discount) })
            {
                var change = target - BalanceOf(offer, kind);
                if (change != 0)
                {
                    db.OfferTransactions.Add(new OfferTransaction
                    {
                        OfferId = offer.Id, UserId = userId, Kind = kind, Amount = change, BalanceAfter = target,
                        Reason = OfferEntryReasons.Adjusted, Description = $"{kind} set to ৳{target:0.##} by an admin", CreatedById = adminId, CreatedAt = now,
                    });
                }
            }
            offer.Cashback = cashback;
            offer.Coupon = coupon;
            offer.Discount = discount;
            offer.IsActive = isActive;
            offer.UpdatedAt = now;
            return new(StatusCodes.Status200OK, "Offers updated.");
        });
    }
}
