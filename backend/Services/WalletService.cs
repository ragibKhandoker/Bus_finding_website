using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

// The outcome of a wallet operation: the HTTP status to answer with, a message for the user, and on success the
// updated wallet and the ledger entry that recorded the change.
public record WalletResult(int StatusCode, string Message, Wallet? Wallet = null, WalletTransaction? Transaction = null)
{
    public bool Succeeded => StatusCode < 300;
}

// All balance and gem changes go through here so every change is recorded as a WalletTransaction, together with
// the balances after it.
//
// The low-level helpers (Credit, TryDebit, AddGems) only change tracked entities: their callers save, so a wallet
// change and the rest of the caller's work are written in one SaveChangesAsync (one database transaction).
// The *Async operations used by the wallet page save themselves. A wallet row carries a row version (xmin), so two
// requests that change the same wallet at once cannot both be written: the loser gets a concurrency conflict and is
// retried against the fresh balance, which is how two deductions can never spend the same money twice.
public class WalletService(AppDbContext db, IOptions<RewardOptions> rewards)
{
    private const int MaxAttempts = 3;

    public RewardOptions Rewards { get; } = rewards.Value;

    public async Task<Wallet> GetOrCreateAsync(Guid userId)
    {
        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
        if (wallet is null)
        {
            wallet = new Wallet { UserId = userId };
            db.Wallets.Add(wallet);
        }
        return wallet;
    }

    // The user's wallet, created (and saved) if they do not have one yet. Safe when called twice at once.
    public async Task<Wallet> GetWalletAsync(Guid userId, CancellationToken ct = default)
    {
        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        if (wallet is not null)
        {
            return wallet;
        }

        wallet = new Wallet { UserId = userId };
        db.Wallets.Add(wallet);
        try
        {
            await db.SaveChangesAsync(ct);
            return wallet;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Another request created it first (the unique index on user_id allows only one).
            db.ChangeTracker.Clear();
            return await db.Wallets.FirstAsync(w => w.UserId == userId, ct);
        }
    }

    // Creates a wallet for every user who has none. Idempotent and safe to run while users sign up:
    // ON CONFLICT skips a user whose wallet appeared in the meantime. Returns how many were created.
    public Task<int> CreateMissingWalletsAsync(CancellationToken ct = default) =>
        db.Database.ExecuteSqlRawAsync("""
            INSERT INTO wallets (id, user_id, balance, gems, currency, last_updated, created_at, updated_at)
            SELECT gen_random_uuid(), u.id, 0, 0, 'BDT', now(), now(), now()
            FROM users u
            WHERE NOT EXISTS (SELECT 1 FROM wallets w WHERE w.user_id = u.id)
            ON CONFLICT (user_id) DO NOTHING
            """, ct);

    public async Task<Offer> GetOrCreateOffersAsync(Guid userId)
    {
        var offer = await db.Offers.Include(o => o.Wallet).FirstOrDefaultAsync(o => o.UserId == userId);
        if (offer is null)
        {
            var wallet = await GetOrCreateAsync(userId);
            offer = new Offer
            {
                UserId = userId,
                Wallet = wallet,
                Cashback = Rewards.WelcomeCashback,
                Coupon = Rewards.WelcomeCoupon,
                Discount = Rewards.WelcomeDiscount,
            };
            db.Offers.Add(offer);
        }
        return offer;
    }

    public void Credit(Wallet wallet, decimal amount, string type, string description)
    {
        wallet.Balance += amount;
        Touch(wallet);
        Record(wallet, type, amount, 0, description);
    }

    public bool TryDebit(Wallet wallet, decimal amount, string type, string description) =>
        Debit(wallet, amount, type, description) is not null;

    // The ledger entry of the debit, or null when the balance is too low (nothing is changed then).
    private WalletTransaction? Debit(Wallet wallet, decimal amount, string type, string description)
    {
        if (amount <= 0 || wallet.Balance < amount)
        {
            return null;
        }
        wallet.Balance -= amount;
        Touch(wallet);
        return Record(wallet, type, -amount, 0, description);
    }

    // tripId marks a trip reward; the ledger allows one per trip.
    // Also tells the user about the reward; the notification is saved with the gems by the caller's SaveChanges.
    public void AddGems(Wallet wallet, int gems, string description, Guid? tripId = null)
    {
        wallet.Gems += gems;
        Touch(wallet);
        Record(wallet, WalletTransactionTypes.GemsEarned, 0, gems, description, tripId);
        if (gems > 0)
        {
            db.Notifications.Add(new Notification
            {
                UserId = wallet.UserId,
                Title = "You earned gems",
                Message = $"You earned {gems} gems: {description}.",
                Type = NotificationTypes.Success,
                Category = NotificationCategories.Reward,
                ActionUrl = "/wallet",
                RelatedId = tripId,
            });
        }
    }

    // How a number of gems converts: only whole blocks of GemsPerTaka gems become taka; the rest stay as gems.
    public (int GemsUsed, decimal Taka) ConversionFor(int gems)
    {
        var rate = Rewards.GemsPerTaka;
        if (rate <= 0 || gems <= 0)
        {
            return (0, 0);
        }
        var blocks = gems / rate;
        return (blocks * rate, blocks);
    }

    // Converts the user's gems to taka. gems: how many gems to convert (null converts as many as possible);
    // only whole GemsPerTaka blocks are used, so the gems left over (and any remainder of the request) stay.
    public async Task<WalletResult> ConvertGemsAsync(Guid userId, int? gems, CancellationToken ct = default)
    {
        var rate = Rewards.GemsPerTaka;
        if (rate <= 0)
        {
            return new WalletResult(StatusCodes.Status503ServiceUnavailable, "Gem conversion is not available: the conversion rate (Rewards:GemsPerTaka) is not configured.");
        }
        if (gems is <= 0)
        {
            return new WalletResult(StatusCodes.Status400BadRequest, "Enter a number of gems greater than zero.");
        }

        return await WithRetryAsync(userId, wallet =>
        {
            if (gems > wallet.Gems)
            {
                return new WalletResult(StatusCodes.Status400BadRequest, $"You only have {wallet.Gems} gems.");
            }
            var (gemsUsed, taka) = ConversionFor(gems ?? wallet.Gems);
            if (gemsUsed == 0)
            {
                return new WalletResult(StatusCodes.Status400BadRequest, $"You need at least {rate} gems to convert. {rate} gems = ৳1.");
            }
            wallet.Gems -= gemsUsed;
            wallet.Balance += taka;
            Touch(wallet);
            var entry = Record(wallet, WalletTransactionTypes.GemsConverted, taka, -gemsUsed, $"Converted {gemsUsed} gems to ৳{taka:0}");
            return new WalletResult(StatusCodes.Status200OK, $"Converted {gemsUsed} gems to ৳{taka:0}.", wallet, entry);
        }, ct);
    }

    // Pays an amount from the user's own wallet. Never lets the balance go below zero.
    public async Task<WalletResult> DeductAsync(Guid userId, decimal amount, string? description, CancellationToken ct = default)
    {
        if (amount <= 0)
        {
            return new WalletResult(StatusCodes.Status400BadRequest, "Amount must be greater than zero.");
        }
        if (decimal.Round(amount, 2) != amount)
        {
            return new WalletResult(StatusCodes.Status400BadRequest, "Amount can have at most 2 decimal places (poisha).");
        }
        var text = string.IsNullOrWhiteSpace(description) ? "Fare payment" : description.Trim();

        return await WithRetryAsync(userId, wallet =>
        {
            if (Debit(wallet, amount, WalletTransactionTypes.Fare, text) is not { } entry)
            {
                return new WalletResult(StatusCodes.Status400BadRequest, "Insufficient wallet balance.", wallet);
            }
            return new WalletResult(StatusCodes.Status200OK, $"৳{amount:0.##} paid from your wallet.", wallet, entry);
        }, ct);
    }

    // The user's ledger, newest first.
    public async Task<(List<WalletTransaction> Items, int Total)> TransactionsAsync(Guid userId, int page, int pageSize, CancellationToken ct = default)
    {
        var walletId = await db.Wallets.Where(w => w.UserId == userId).Select(w => (Guid?)w.Id).FirstOrDefaultAsync(ct);
        if (walletId is null)
        {
            return ([], 0);
        }
        var query = db.WalletTransactions.Where(t => t.WalletId == walletId);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(ct);
        return (items, total);
    }

    // Loads the wallet fresh, applies the change and saves it. If another request changed the same wallet in
    // between, nothing of this attempt was written; it is run again against the new balance.
    private async Task<WalletResult> WithRetryAsync(Guid userId, Func<Wallet, WalletResult> apply, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            db.ChangeTracker.Clear();
            var wallet = await GetWalletAsync(userId, ct);
            var result = apply(wallet);
            if (!result.Succeeded)
            {
                db.ChangeTracker.Clear();
                return result;
            }
            try
            {
                await db.SaveChangesAsync(ct);
                return result;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                // Retry with the balance the other request left.
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
                return new WalletResult(StatusCodes.Status409Conflict, "Your wallet is busy with another payment. Please try again.");
            }
        }
    }

    private static void Touch(Wallet wallet)
    {
        wallet.LastUpdated = DateTime.UtcNow;
        wallet.UpdatedAt = DateTime.UtcNow;
    }

    private WalletTransaction Record(Wallet wallet, string type, decimal amount, int gems, string description, Guid? tripId = null)
    {
        var entry = new WalletTransaction
        {
            Wallet = wallet,
            Type = type,
            Amount = amount,
            GemsChange = gems,
            BalanceAfter = wallet.Balance,
            GemsAfter = wallet.Gems,
            Description = description.Length > 200 ? description[..200] : description,
            TripId = tripId,
        };
        db.WalletTransactions.Add(entry);
        return entry;
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
