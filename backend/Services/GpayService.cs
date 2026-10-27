using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

public record GpayResult(int StatusCode, string Message, GpayAccount? Account = null, GpayTransaction? Entry = null, string? Pin = null, Wallet? Wallet = null)
{
    public bool Succeeded => StatusCode is >= 200 and < 300;
}

public record GpayTripView(Guid TripId, string BusName, string Source, string Destination, DateTime? EndedAt, decimal Fare, DateTime? PaidAt);

// GPay, the in-app stored-value account. There is no real payment provider: an account starts with
// Rewards:GpayStartingBalance, and only an admin can add money (a test top-up). Money leaves GPay by moving it into
// the commute wallet, paying a completed trip's fare, or (older clients) paying an amount.
//
// Moving money needs the account's 4-digit PIN. Each change takes the balance with one conditional UPDATE
// (balance >= amount) inside a transaction that also writes the ledger and any wallet credit, so a repeated or
// simultaneous request can never overspend, and nothing is saved unless all of it is.
public class GpayService(AppDbContext db, WalletService wallets, NotificationService notifications)
{
    public const decimal MaxAmount = 100_000m;
    private static readonly PasswordHasher<GpayAccount> PinHasher = new();

    public decimal StartingBalance => wallets.Rewards.GpayStartingBalance;

    public Task<GpayAccount?> FindAsync(Guid userId) => db.GpayAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == userId);

    private static string NewPin() => RandomNumberGenerator.GetInt32(0, 10000).ToString("D4");

    // Creates the account with the configured starting balance and returns its PIN once; only a hash is stored.
    public async Task<GpayResult> RegisterAsync(Guid userId)
    {
        if (await db.GpayAccounts.AnyAsync(a => a.UserId == userId))
        {
            return new(StatusCodes.Status409Conflict, "You already have a GPay account. Unlock it with your PIN.");
        }
        var pin = NewPin();
        var account = new GpayAccount { UserId = userId, Balance = StartingBalance };
        account.PinHash = PinHasher.HashPassword(account, pin);
        db.GpayAccounts.Add(account);
        db.GpayTransactions.Add(new GpayTransaction
        {
            Account = account, UserId = userId, Type = GpayTransactionTypes.Opening, Amount = account.Balance,
            BalanceAfter = account.Balance, Description = "Starting balance",
        });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return new(StatusCodes.Status409Conflict, "You already have a GPay account. Unlock it with your PIN.");
        }
        return new(StatusCodes.Status201Created, "GPay account created. Save your PIN; it will not be shown again.", account, Pin: pin);
    }

    public async Task<GpayResult> CheckPinAsync(Guid userId, string? pin)
    {
        var account = await FindAsync(userId);
        if (account is null)
        {
            return new(StatusCodes.Status404NotFound, "GPay account not found.");
        }
        // 400 rather than 401: the web client treats 401 as an expired sign-in and signs the user out.
        if (string.IsNullOrEmpty(pin) || PinHasher.VerifyHashedPassword(account, account.PinHash, pin) == PasswordVerificationResult.Failed)
        {
            return new(StatusCodes.Status400BadRequest, "Incorrect PIN.");
        }
        return new(StatusCodes.Status200OK, "GPay unlocked.", account);
    }

    // A forgotten PIN: the user proves who they are with their account password and gets a new PIN once.
    public async Task<GpayResult> ResetPinAsync(Guid userId, string password)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        var account = await db.GpayAccounts.FirstOrDefaultAsync(a => a.UserId == userId);
        if (user is null || account is null)
        {
            return new(StatusCodes.Status404NotFound, "GPay account not found.");
        }
        if (new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            return new(StatusCodes.Status400BadRequest, "Your account password is incorrect.");
        }
        var pin = NewPin();
        account.PinHash = PinHasher.HashPassword(account, pin);
        account.UpdatedAt = DateTime.UtcNow;
        notifications.Notify(userId, "GPay PIN changed", "Your GPay PIN was reset. If this was not you, change your password.",
            NotificationTypes.Warning, "/gpay", NotificationCategories.Account);
        await db.SaveChangesAsync();
        return new(StatusCodes.Status200OK, "New PIN created. Save it; it will not be shown again.", account, Pin: pin);
    }

    public async Task<(List<GpayTransaction> Items, int Total)> HistoryAsync(Guid userId, int page, int pageSize)
    {
        var query = db.GpayTransactions.Where(x => x.UserId == userId);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).AsNoTracking().ToListAsync();
        return (items, total);
    }

    // The user's recent completed trips with a fare, and when each was paid with GPay (null when not yet).
    public async Task<List<GpayTripView>> TripsAsync(Guid userId, int take = 20)
    {
        var trips = await db.Trips
            .Where(t => t.UserId == userId && t.Status == TripStatus.Completed && t.Fare > 0)
            .OrderByDescending(t => t.EndedAt ?? t.CreatedAt)
            .Take(take)
            .Select(t => new { t.Id, t.BusName, t.Source, t.Destination, t.EndedAt, t.Fare })
            .AsNoTracking()
            .ToListAsync();
        var ids = trips.Select(t => t.Id).ToList();
        var paid = await db.GpayTransactions.Where(x => x.TripId != null && ids.Contains(x.TripId.Value))
            .ToDictionaryAsync(x => x.TripId!.Value, x => x.CreatedAt);
        return trips.Select(t => new GpayTripView(t.Id, t.BusName, t.Source, t.Destination, t.EndedAt, t.Fare,
            paid.TryGetValue(t.Id, out var at) ? at : null)).ToList();
    }

    private static string? AmountProblem(decimal amount) =>
        amount <= 0 ? "Amount must be greater than zero."
        : amount > MaxAmount ? $"The most you can move at once is ৳{MaxAmount:0}."
        : decimal.Round(amount, 2) != amount ? "Amounts can have at most 2 decimal places (poisha)."
        : null;

    private static string? Clean(string? reference) => string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();

    // Runs one money movement. A request repeated with the same reference returns the first result; a trip already
    // paid, a wallet changed at the same moment, or any failure rolls everything back.
    private async Task<GpayResult> MoveAsync(Guid userId, string? pin, string? reference, Func<GpayAccount, DateTime, Task<GpayResult>> work)
    {
        var check = await CheckPinAsync(userId, pin);
        if (!check.Succeeded)
        {
            return check;
        }
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            if (reference is not null && await db.GpayTransactions.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId && x.Reference == reference) is { } earlier)
            {
                return new GpayResult(StatusCodes.Status200OK, "This request was already processed.", await FindAsync(userId), earlier);
            }
            await using var transaction = await db.Database.BeginTransactionAsync();
            GpayResult result;
            try
            {
                result = await work(check.Account!, DateTime.UtcNow);
                if (!result.Succeeded)
                {
                    return result;
                }
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ix_gpay_transactions_one_per_trip" })
            {
                await transaction.RollbackAsync();
                return new GpayResult(StatusCodes.Status409Conflict, "This trip is already paid.");
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ix_gpay_transactions_one_per_reference" })
            {
                await transaction.RollbackAsync();
                return new GpayResult(StatusCodes.Status200OK, "This request was already processed.", await FindAsync(userId));
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync();
                return new GpayResult(StatusCodes.Status409Conflict, "Your wallet changed at the same moment. Nothing was moved; please try again.");
            }
            db.ChangeTracker.Clear();
            return result with { Account = await FindAsync(userId) };
        });
    }

    // Takes amount off the balance only if that much is there. Returns the new balance, or null when it is not.
    private async Task<decimal?> TakeAsync(Guid accountId, decimal amount, DateTime now)
    {
        var changed = await db.GpayAccounts.Where(a => a.Id == accountId && a.Balance >= amount)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Balance, a => a.Balance - amount).SetProperty(a => a.UpdatedAt, now));
        return changed == 0 ? null : await db.GpayAccounts.AsNoTracking().Where(a => a.Id == accountId).Select(a => a.Balance).SingleAsync();
    }

    // Moves money from GPay into the commute wallet.
    public Task<GpayResult> RechargeWalletAsync(Guid userId, decimal amount, string? pin, string? reference)
    {
        if (AmountProblem(amount) is { } problem)
        {
            return Task.FromResult(new GpayResult(StatusCodes.Status400BadRequest, problem));
        }
        return MoveAsync(userId, pin, Clean(reference), async (account, now) =>
        {
            if (await TakeAsync(account.Id, amount, now) is not { } left)
            {
                return new GpayResult(StatusCodes.Status400BadRequest, "Insufficient GPay balance.");
            }
            var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId) ?? await wallets.GetOrCreateAsync(userId);
            var credit = wallets.Credit(wallet, amount, WalletTransactionTypes.Recharge, "Recharge from GPay");
            var entry = new GpayTransaction
            {
                AccountId = account.Id, UserId = userId, Type = GpayTransactionTypes.WalletRecharge, Amount = -amount, BalanceAfter = left,
                Description = "Moved to commute wallet", WalletTransaction = credit, Reference = Clean(reference), CreatedAt = now,
            };
            db.GpayTransactions.Add(entry);
            return new GpayResult(StatusCodes.Status200OK, $"৳{amount:0.##} added to your wallet.", Entry: entry, Wallet: wallet);
        });
    }

    // Pays a completed trip's fare, as the server recorded it when the trip ended. Each trip can be paid once.
    public async Task<GpayResult> PayTripAsync(Guid userId, Guid tripId, string? pin, string? reference)
    {
        var trip = await db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripId && t.UserId == userId);
        if (trip is null)
        {
            return new(StatusCodes.Status404NotFound, "Trip not found.");
        }
        if (trip.Status != TripStatus.Completed || trip.Fare <= 0)
        {
            return new(StatusCodes.Status409Conflict, "Only a completed trip with a fare can be paid.");
        }
        if (await db.GpayTransactions.AnyAsync(x => x.TripId == tripId))
        {
            return new(StatusCodes.Status409Conflict, "This trip is already paid.");
        }
        return await MoveAsync(userId, pin, Clean(reference), async (account, now) =>
        {
            if (await TakeAsync(account.Id, trip.Fare, now) is not { } left)
            {
                return new GpayResult(StatusCodes.Status400BadRequest, $"Insufficient GPay balance: the fare is ৳{trip.Fare:0.##}.");
            }
            var entry = new GpayTransaction
            {
                AccountId = account.Id, UserId = userId, Type = GpayTransactionTypes.TripPayment, Amount = -trip.Fare, BalanceAfter = left,
                Description = $"Fare: {trip.BusName}, {trip.Source} to {trip.Destination}", TripId = tripId, Reference = Clean(reference), CreatedAt = now,
            };
            db.GpayTransactions.Add(entry);
            return new GpayResult(StatusCodes.Status200OK, $"Paid ৳{trip.Fare:0.##} for your trip.", Entry: entry);
        });
    }

    // Older clients' /gpay/deduct: pays an amount the user chooses from their own balance.
    public Task<GpayResult> PayAmountAsync(Guid userId, decimal amount, string? description, string? pin, string? reference)
    {
        if (AmountProblem(amount) is { } problem)
        {
            return Task.FromResult(new GpayResult(StatusCodes.Status400BadRequest, problem));
        }
        return MoveAsync(userId, pin, Clean(reference), async (account, now) =>
        {
            if (await TakeAsync(account.Id, amount, now) is not { } left)
            {
                return new GpayResult(StatusCodes.Status400BadRequest, "Insufficient GPay balance.");
            }
            var text = string.IsNullOrWhiteSpace(description) ? "Payment" : description.Trim();
            var entry = new GpayTransaction
            {
                AccountId = account.Id, UserId = userId, Type = GpayTransactionTypes.Payment, Amount = -amount, BalanceAfter = left,
                Description = text.Length > 200 ? text[..200] : text, Reference = Clean(reference), CreatedAt = now,
            };
            db.GpayTransactions.Add(entry);
            return new GpayResult(StatusCodes.Status200OK, "Payment completed.", Entry: entry);
        });
    }

    // Admin only: adds test money to a user's GPay. There is no payment provider, so this is the only way money is
    // added after the starting balance. A repeated reference does nothing the second time.
    public async Task<GpayResult> TopUpAsync(Guid adminId, Guid userId, decimal amount, string? reference)
    {
        if (AmountProblem(amount) is { } problem)
        {
            return new(StatusCodes.Status400BadRequest, problem);
        }
        var account = await FindAsync(userId);
        if (account is null)
        {
            return new(StatusCodes.Status404NotFound, "That user has no GPay account.");
        }
        reference = Clean(reference);
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            if (reference is not null && await db.GpayTransactions.AnyAsync(x => x.UserId == userId && x.Reference == reference))
            {
                return new GpayResult(StatusCodes.Status200OK, "This top-up was already made.", await FindAsync(userId));
            }
            await using var transaction = await db.Database.BeginTransactionAsync();
            var now = DateTime.UtcNow;
            await db.GpayAccounts.Where(a => a.Id == account.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Balance, a => a.Balance + amount).SetProperty(a => a.UpdatedAt, now));
            var balance = await db.GpayAccounts.AsNoTracking().Where(a => a.Id == account.Id).Select(a => a.Balance).SingleAsync();
            db.GpayTransactions.Add(new GpayTransaction
            {
                AccountId = account.Id, UserId = userId, Type = GpayTransactionTypes.TopUp, Amount = amount, BalanceAfter = balance,
                Description = "Test top-up by an admin", Reference = reference, CreatedById = adminId, CreatedAt = now,
            });
            notifications.Notify(userId, "GPay topped up", $"৳{amount:0.##} was added to your GPay by SmartCommute.",
                NotificationTypes.Success, "/gpay", NotificationCategories.Account);
            try
            {
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                await transaction.RollbackAsync();
                return new GpayResult(StatusCodes.Status200OK, "This top-up was already made.", await FindAsync(userId));
            }
            return new GpayResult(StatusCodes.Status200OK, $"৳{amount:0.##} added to GPay.", await FindAsync(userId));
        });
    }
}
