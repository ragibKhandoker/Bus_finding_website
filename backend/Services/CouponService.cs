using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

// What applying a coupon to a trip would do (or did): the original fare, what was already taken off, the saving,
// what is left to pay, and the cashback the trip earned. Everything is the server's own numbers.
public record CouponQuote(bool Ok, string? Error, Coupon? Coupon, Guid? TripId, decimal Fare, decimal CoveredBefore, decimal Discount,
    decimal FinalFare, decimal CashbackEarned, string? TripLabel, int StatusCode = StatusCodes.Status200OK)
{
    public static CouponQuote Fail(string error, int status = StatusCodes.Status400BadRequest, Coupon? coupon = null) =>
        new(false, error, coupon, null, 0, 0, 0, 0, 0, null, status);
}

public record CouponRedeemResult(int StatusCode, string Message, CouponRedemption? Redemption = null, CouponQuote? Quote = null, Wallet? Wallet = null)
{
    public bool Succeeded => StatusCode is >= 200 and < 300;
}

// One coupon as the user sees it. Status: available, locked (they do not qualify yet, e.g. a Gold-only coupon),
// used or expired.
public record CouponListItem(Coupon Coupon, string Status, string? Note, int UsesLeft, int? TotalLeft, DateTime? ExpiresAt,
    UserCoupon? Copy, CouponRedemption? Redemption);

public record CouponLists(List<CouponListItem> Available, List<CouponListItem> Used, List<CouponListItem> Expired);

// A completed eligible trip a coupon can be used on, with what has been taken off it so far.
public record EligibleTripView(Guid TripId, string BusName, string? RouteNumber, string Source, string Destination, DateTime EndedAt,
    decimal Fare, decimal CouponSavings, decimal CreditSavings, decimal Cashback, bool CashbackProcessed, bool Paid, bool CanUseCoupon,
    List<string> Coupons)
{
    public decimal Covered => CouponSavings + CreditSavings;
    public decimal LeftToPay => Math.Max(0, Fare - Covered);
}

// Who the user is, for coupon rules.
internal record CouponUser(DateTime CreatedAt, string? TierCode, int EligibleTrips);

// Coupon codes: listing, checking, redeeming and issuing. A coupon is used on one of the user's completed eligible
// trips: either after the trip (the saving is paid into the wallet balance) or while paying the fare with GPay (the
// saving comes off the payment).
//
// Concurrency: a redemption runs in a transaction that first locks the user's row, so the same user's requests
// run one after another (per-user limits, one coupon per trip). The overall limit is a conditional UPDATE
// (redemption_count < total_limit) that the database also guards with a check constraint, so when many users race
// for the last coupon only the ones within the limit succeed. Unique keys stop the same coupon on the same trip,
// and a repeated client reference, from being recorded twice.
public class CouponService(AppDbContext db, WalletService wallets, NotificationService notifications, RewardConfigService config)
{
    // Coupons can be used on trips that ended in the last week.
    public static readonly TimeSpan TripWindow = TimeSpan.FromDays(7);

    private async Task<CouponUser?> UserAsync(Guid userId) => await db.Users.Where(u => u.Id == userId)
        .Select(u => new CouponUser(
            u.CreatedAt,
            db.UserMemberships.Where(m => m.UserId == userId).Select(m => m.TierCode).FirstOrDefault(),
            db.TripRewards.Count(r => r.UserId == userId && r.Eligible)))
        .AsNoTracking().FirstOrDefaultAsync();

    private static string Label(Trip t) => $"{t.BusName}: {t.Source} to {t.Destination}";

    // ---- Lists ----

    public async Task<CouponLists> ListAsync(Guid userId, DateTime now)
    {
        var user = await UserAsync(userId);
        var settings = await config.SettingsAsync();
        var tiers = await config.TiersAsync();
        var copies = await db.UserCoupons.Where(u => u.UserId == userId).AsNoTracking().ToListAsync();
        var copyIds = copies.Select(c => c.CouponId).Distinct().ToList();
        var recent = now.AddDays(-30);
        var coupons = await db.Coupons.Include(c => c.Campaign)
            .Where(c => c.DeletedAt == null && (c.Audience != CouponAudiences.Assigned || copyIds.Contains(c.Id))
                && (c.ExpiresAt == null || c.ExpiresAt > recent || copyIds.Contains(c.Id)))
            .AsNoTracking().ToListAsync();
        var redemptions = await db.CouponRedemptions.Include(r => r.Coupon).Include(r => r.Trip)
            .Where(r => r.UserId == userId).OrderByDescending(r => r.CreatedAt).Take(100)
            .AsNoTracking().ToListAsync();
        var usedCount = redemptions.GroupBy(r => r.CouponId).ToDictionary(g => g.Key, g => g.Count());

        var tierRank = PromotionRules.RankOf(tiers, user?.TierCode);
        var isNew = user is not null && PromotionRules.IsNewUser(user.CreatedAt, now, settings, user.EligibleTrips);
        var available = new List<CouponListItem>();
        var expired = new List<CouponListItem>();
        foreach (var coupon in coupons)
        {
            var mine = copies.Where(c => c.CouponId == coupon.Id).OrderBy(c => c.UsedAt != null).ThenByDescending(c => c.IssuedAt).FirstOrDefault();
            var used = usedCount.GetValueOrDefault(coupon.Id);
            var usesLeft = Math.Max(0, coupon.PerUserLimit - used);
            int? totalLeft = coupon.TotalLimit is { } total ? Math.Max(0, total - coupon.RedemptionCount) : null;
            var expiresAt = Earliest(coupon.ExpiresAt, mine?.ExpiresAt, coupon.Campaign?.EndsAt);
            if (usesLeft == 0 || mine?.UsedAt is not null && coupon.Audience == CouponAudiences.Assigned)
            {
                continue; // already in Used
            }
            var gone = expiresAt is { } end && end <= now || coupon.Campaign?.Status == CampaignStatus.Expired;
            if (gone || totalLeft == 0)
            {
                expired.Add(new CouponListItem(coupon, "expired", totalLeft == 0 && !gone ? "Fully redeemed" : "Expired", usesLeft, totalLeft, expiresAt, mine, null));
                continue;
            }
            if (!coupon.IsActive || coupon.StartsAt > now)
            {
                continue; // switched off, or not started: not shown
            }
            var problem = PromotionRules.CouponProblem(coupon, coupon.Campaign, now, used, mine, tierRank, isNew, tiers);
            available.Add(new CouponListItem(coupon, problem is null ? "available" : "locked", problem, usesLeft, totalLeft, expiresAt, mine, null));
        }
        var usedItems = redemptions.Select(r => new CouponListItem(r.Coupon!, "used",
            r.Trip is null ? null : Label(r.Trip), 0, null, null, null, r)).ToList();
        return new CouponLists(
            available.OrderBy(i => i.Status == "locked").ThenBy(i => i.ExpiresAt ?? DateTime.MaxValue).ToList(),
            usedItems,
            expired.OrderByDescending(i => i.ExpiresAt).ToList());
    }

    private static DateTime? Earliest(params DateTime?[] dates) =>
        dates.Where(d => d is not null).Select(d => d!.Value).DefaultIfEmpty(DateTime.MaxValue).Min() is var min && min == DateTime.MaxValue ? null : min;

    // ---- Trips ----

    // The user's recent completed eligible trips (boarded and ended in the app), newest first.
    public async Task<List<EligibleTripView>> EligibleTripsAsync(Guid userId, DateTime now, int take = 10)
    {
        var since = now - TripWindow;
        var rows = await db.Trips
            .Where(t => t.UserId == userId && t.Status == TripStatus.Completed && t.IndividualBusId != null && t.Fare > 0 && t.EndedAt != null)
            .OrderByDescending(t => t.EndedAt).Take(take)
            .Select(t => new
            {
                t.Id, t.BusName, t.RouteNumber, t.Source, t.Destination, EndedAt = t.EndedAt!.Value, t.Fare,
                Coupons = db.CouponRedemptions.Where(r => r.TripId == t.Id).Sum(r => (decimal?)r.Discount) ?? 0,
                Credits = db.OfferTransactions.Where(x => x.TripId == t.Id).Sum(x => (decimal?)x.Amount) ?? 0,
                Reward = db.TripRewards.Where(r => r.TripId == t.Id).Select(r => (decimal?)r.Cashback).FirstOrDefault(),
                Paid = db.GpayTransactions.Any(g => g.TripId == t.Id),
                Codes = db.CouponRedemptions.Where(r => r.TripId == t.Id).Select(r => r.Coupon!.Code).ToList(),
            })
            .AsNoTracking().AsSingleQuery().ToListAsync();
        return rows.Select(r => new EligibleTripView(r.Id, r.BusName, r.RouteNumber, r.Source, r.Destination, r.EndedAt, r.Fare, r.Coupons, -r.Credits,
            r.Reward ?? 0, r.Reward is not null, r.Paid, r.EndedAt >= since && r.Fare - r.Coupons + r.Credits > 0, r.Codes)).ToList();
    }

    // ---- Checking ----

    public async Task<CouponQuote> QuoteAsync(Guid userId, string? code, Guid? tripId, DateTime now)
    {
        var normalized = PromotionRules.NormalizeCode(code);
        if (normalized is null)
        {
            return CouponQuote.Fail(string.IsNullOrWhiteSpace(code) ? "Enter a coupon code." : "That is not a valid coupon code. Codes use letters and numbers, like DHAKA10.");
        }
        var coupon = await db.Coupons.Include(c => c.Campaign).AsNoTracking().FirstOrDefaultAsync(c => c.Code == normalized && c.DeletedAt == null);
        if (coupon is null)
        {
            return CouponQuote.Fail("That coupon code does not exist. Check the spelling and try again.", StatusCodes.Status404NotFound);
        }
        if (tripId is null)
        {
            return CouponQuote.Fail("Choose the completed trip to use the coupon on.", coupon: coupon);
        }
        var trip = await db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripId && t.UserId == userId);
        if (trip is null)
        {
            return CouponQuote.Fail("Trip not found.", StatusCodes.Status404NotFound, coupon);
        }
        return await QuoteForTripAsync(userId, coupon, trip, now);
    }

    private async Task<CouponQuote> QuoteForTripAsync(Guid userId, Coupon coupon, Trip trip, DateTime now)
    {
        if (PromotionRules.TripProblem(trip, DateTime.MinValue) is { } tripProblem)
        {
            return CouponQuote.Fail("Coupons work on completed bus trips you boarded and ended in the app. " + tripProblem, StatusCodes.Status409Conflict, coupon);
        }
        if (trip.EndedAt < now - TripWindow)
        {
            return CouponQuote.Fail("Coupons can only be used on trips from the last 7 days.", StatusCodes.Status409Conflict, coupon);
        }
        var settings = await config.SettingsAsync();
        var tiers = await config.TiersAsync();
        var couponId = coupon.Id;
        var tripId = trip.Id;
        // One round trip for everything about the user and the trip.
        var facts = await db.Users.Where(u => u.Id == userId).Select(u => new
        {
            u.CreatedAt,
            Tier = db.UserMemberships.Where(m => m.UserId == userId).Select(m => m.TierCode).FirstOrDefault(),
            Trips = db.TripRewards.Count(r => r.UserId == userId && r.Eligible && r.TripId != tripId),
            Used = db.CouponRedemptions.Count(r => r.UserId == userId && r.CouponId == couponId),
            SameCoupon = db.CouponRedemptions.Any(r => r.TripId == tripId && r.CouponId == couponId),
            OtherCoupons = db.CouponRedemptions.Count(r => r.TripId == tripId),
            CouponSavings = db.CouponRedemptions.Where(r => r.TripId == tripId).Sum(r => (decimal?)r.Discount) ?? 0,
            Credits = db.OfferTransactions.Where(x => x.TripId == tripId).Sum(x => (decimal?)x.Amount) ?? 0,
            Cashback = db.TripRewards.Where(r => r.TripId == tripId).Select(r => (decimal?)r.Cashback).FirstOrDefault() ?? 0,
        }).AsNoTracking().FirstAsync();
        if (facts.SameCoupon)
        {
            return CouponQuote.Fail("You already used this coupon on this trip.", StatusCodes.Status409Conflict, coupon);
        }
        var copy = await db.UserCoupons.AsNoTracking().Where(c => c.UserId == userId && c.CouponId == couponId)
            .OrderBy(c => c.UsedAt != null).ThenByDescending(c => c.IssuedAt).FirstOrDefaultAsync();
        var covered = facts.CouponSavings - facts.Credits;
        var tierRank = PromotionRules.RankOf(tiers, facts.Tier);
        var ctx = new CouponContext(now, trip.Fare, covered, trip.EndedAt!.Value, trip.RouteNumber, facts.Used, copy, tierRank,
            PromotionRules.IsNewUser(facts.CreatedAt, now, settings, facts.Trips), facts.OtherCoupons > 0, facts.Cashback > 0, settings, tiers);
        var check = PromotionRules.Check(coupon, coupon.Campaign, ctx);
        return check.Ok
            ? new CouponQuote(true, null, coupon, trip.Id, trip.Fare, covered, check.Discount, check.FinalFare, facts.Cashback, Label(trip))
            : new CouponQuote(false, check.Error, coupon, trip.Id, trip.Fare, covered, 0, Math.Max(0, trip.Fare - covered), facts.Cashback, Label(trip),
                StatusCodes.Status409Conflict);
    }

    // The coupon that saves the most on this trip, among the public ones and those issued to the user.
    public async Task<CouponQuote?> BestAsync(Guid userId, Guid tripId, DateTime now)
    {
        var trip = await db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripId && t.UserId == userId);
        if (trip is null)
        {
            return null;
        }
        var lists = await ListAsync(userId, now);
        CouponQuote? best = null;
        foreach (var item in lists.Available.Where(i => i.Status == "available"))
        {
            var quote = await QuoteForTripAsync(userId, item.Coupon, trip, now);
            if (quote.Ok && (best is null || quote.Discount > best.Discount))
            {
                best = quote;
            }
        }
        return best;
    }

    // ---- Redeeming ----

    // After the trip: the saving is paid into the wallet balance. reference (a random id per button press) makes a
    // repeated request return the first result.
    public async Task<CouponRedeemResult> RedeemAsync(Guid userId, string? code, Guid? tripId, string? reference)
    {
        reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        if (reference is not null && await RepeatAsync(userId, reference) is { } repeat)
        {
            return repeat;
        }
        var now = DateTime.UtcNow;
        var first = await QuoteAsync(userId, code, tripId, now);
        if (!first.Ok)
        {
            return new(first.StatusCode, first.Error!, Quote: first);
        }
        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync();
                await LockUsersAsync(userId);
                var result = await RedeemInTransactionAsync(userId, code, tripId!.Value, RedemptionModes.Wallet, now, reference);
                if (!result.Succeeded)
                {
                    await transaction.RollbackAsync();
                    return result;
                }
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
                return result;
            });
        }
        catch (DbUpdateException ex) when (IsUnique(ex, "ix_coupon_redemptions_one_per_reference"))
        {
            db.ChangeTracker.Clear();
            return await RepeatAsync(userId, reference!) ?? new(StatusCodes.Status409Conflict, "This request was already processed.");
        }
        catch (DbUpdateException ex) when (IsUnique(ex, "ix_coupon_redemptions_one_per_trip"))
        {
            db.ChangeTracker.Clear();
            return new(StatusCodes.Status409Conflict, "You already used this coupon on this trip.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.CheckViolation })
        {
            db.ChangeTracker.Clear();
            return new(StatusCodes.Status409Conflict, "This coupon has just been fully redeemed.");
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return new(StatusCodes.Status409Conflict, "Your wallet changed at the same moment. Nothing was used; please try again.");
        }
    }

    private async Task<CouponRedeemResult?> RepeatAsync(Guid userId, string reference)
    {
        var earlier = await db.CouponRedemptions.Include(r => r.Coupon).AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == userId && r.Reference == reference);
        return earlier is null ? null : new(StatusCodes.Status200OK, "This request was already processed.", earlier,
            Wallet: await db.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.UserId == userId));
    }

    // Locks the users' rows until the transaction ends, always in the same order so two transactions can never wait
    // for each other.
    public async Task LockUsersAsync(params Guid[] userIds)
    {
        foreach (var id in userIds.Distinct().OrderBy(id => id))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM users WHERE id = {id} FOR UPDATE");
        }
    }

    // The redemption itself, inside the caller's transaction (which must already hold the user's row, see
    // LockUsersAsync). Nothing is saved here. On failure the caller must roll back: the coupon's count may already
    // have been raised.
    public async Task<CouponRedeemResult> RedeemInTransactionAsync(Guid userId, string? code, Guid tripId, string mode, DateTime now, string? reference)
    {
        // Checked again now that this user's other requests are held back: the per-user count and the trip's other
        // coupons cannot change until this transaction ends.
        var quote = await QuoteAsync(userId, code, tripId, now);
        if (!quote.Ok)
        {
            return new(quote.StatusCode, quote.Error!, Quote: quote);
        }
        var coupon = quote.Coupon!;
        var claimed = await db.Coupons
            .Where(c => c.Id == coupon.Id && c.IsActive && c.DeletedAt == null && (c.TotalLimit == null || c.RedemptionCount < c.TotalLimit))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.RedemptionCount, c => c.RedemptionCount + 1).SetProperty(c => c.UpdatedAt, now));
        if (claimed == 0)
        {
            return new(StatusCodes.Status409Conflict, "This coupon has just been fully redeemed.", Quote: quote);
        }
        var discount = quote.Discount;
        if (coupon.CampaignId is { } campaignId)
        {
            var counted = await db.RewardCampaigns
                .Where(c => c.Id == campaignId && c.Status == CampaignStatus.Active && c.StartsAt <= now && c.EndsAt > now
                    && (c.Budget == null || c.Spent + discount <= c.Budget) && (c.MaxRedemptions == null || c.Redemptions < c.MaxRedemptions))
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Spent, c => c.Spent + discount).SetProperty(c => c.Redemptions, c => c.Redemptions + 1)
                    .SetProperty(c => c.UpdatedAt, now));
            if (counted == 0)
            {
                return new(StatusCodes.Status409Conflict, "This campaign's rewards have all been given out.", Quote: quote);
            }
        }
        var copy = await db.UserCoupons.Where(c => c.UserId == userId && c.CouponId == coupon.Id && c.UsedAt == null)
            .OrderBy(c => c.ExpiresAt ?? DateTime.MaxValue).FirstOrDefaultAsync();
        if (copy is not null)
        {
            copy.UsedAt = now;
        }
        var redemption = new CouponRedemption
        {
            CouponId = coupon.Id, UserId = userId, TripId = tripId, UserCouponId = copy?.Id, CampaignId = coupon.CampaignId,
            Fare = quote.Fare, Discount = discount, FinalFare = quote.FinalFare, Mode = mode, Reference = reference, CreatedAt = now,
        };
        Wallet? wallet = null;
        if (mode == RedemptionModes.Wallet)
        {
            wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId) ?? await wallets.GetOrCreateAsync(userId);
            var credit = wallets.Credit(wallet, discount, OfferKinds.Coupon, $"Coupon {coupon.Code} on {quote.TripLabel}");
            redemption.WalletTransactionId = credit.Id;
        }
        db.CouponRedemptions.Add(redemption);
        var where = mode == RedemptionModes.Wallet ? "The saving was added to your wallet balance." : "It came off your GPay payment.";
        notifications.Notify(userId, "Coupon redeemed", $"{coupon.Code} saved you {PromotionRules.Money(discount)} on {quote.TripLabel}. {where}",
            NotificationTypes.Success, "/offers", NotificationCategories.Reward, relatedId: tripId);
        redemption.Coupon = coupon;
        var message = $"{coupon.Code} applied: you saved {PromotionRules.Money(discount)}. You pay {PromotionRules.Money(quote.FinalFare)} for this trip.";
        return new(StatusCodes.Status200OK, message, redemption, quote, wallet);
    }

    // ---- Issuing ----

    // Hands a coupon to a user (not saved here). reference is unique per user, so the same reward is issued once;
    // returns null when it already was.
    public async Task<UserCoupon?> IssueAsync(Guid userId, Coupon coupon, string source, string reference, DateTime now, User? newUser = null)
    {
        if (newUser is null && await db.UserCoupons.AnyAsync(c => c.UserId == userId && c.Reference == reference))
        {
            return null;
        }
        DateTime? expires = coupon.ValidDaysAfterIssue is { } days ? now.AddDays(days) : null;
        if (coupon.ExpiresAt is { } end && (expires is null || end < expires))
        {
            expires = end;
        }
        var copy = new UserCoupon { UserId = userId, User = newUser, CouponId = coupon.Id, Source = source, Reference = reference, IssuedAt = now, ExpiresAt = expires };
        db.UserCoupons.Add(copy);
        return copy;
    }

    // The active coupons of one type that are handed out automatically (welcome, first trip, referral).
    public Task<List<Coupon>> AutoIssuedAsync(string type, DateTime now) => db.Coupons.AsNoTracking()
        .Where(c => c.Type == type && c.IsActive && c.DeletedAt == null && c.StartsAt <= now && (c.ExpiresAt == null || c.ExpiresAt > now))
        .ToListAsync();

    // Sign-up: the welcome coupons, saved with the new account.
    public async Task<int> IssueWelcomeAsync(User user, DateTime now)
    {
        var issued = 0;
        foreach (var coupon in await AutoIssuedAsync(CouponTypes.Welcome, now))
        {
            if (await IssueAsync(user.Id, coupon, CouponTypes.Welcome, $"welcome:{coupon.Id}", now, user) is not null)
            {
                issued++;
            }
        }
        if (issued > 0)
        {
            notifications.Notify(user.Id, "Welcome coupon", $"You have {issued} welcome coupon{(issued == 1 ? "" : "s")} for your first Dhaka bus trips. Find {(issued == 1 ? "it" : "them")} on the Offers page.",
                NotificationTypes.Success, "/offers", NotificationCategories.Reward);
        }
        return issued;
    }

    private static bool IsUnique(DbUpdateException ex, string constraint) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg && pg.ConstraintName == constraint;
}
