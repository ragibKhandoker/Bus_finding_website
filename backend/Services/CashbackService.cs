using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

// The user's cashback balance and where it came from, for the Offers page.
public record CashbackSummary(decimal Balance, decimal TotalEarned, decimal TotalUsed, decimal TotalExpired, decimal EarnedThisMonth,
    decimal? MonthlyLimit, decimal ExpiringSoon, DateTime? NextExpiry, int ExpiryDays);

// The cashback ledger used by OfferService when cashback is moved into the wallet: it takes the amount from the
// lots that expire first. It works on the caller's DbContext and transaction.
public static class CashbackLedger
{
    public static async Task ConsumeAsync(AppDbContext db, Guid userId, decimal amount, DateTime now, WalletTransaction credit)
    {
        var lots = await db.CashbackTransactions
            .Where(c => c.UserId == userId && c.Remaining > 0)
            .OrderBy(c => c.ExpiresAt == null).ThenBy(c => c.ExpiresAt).ThenBy(c => c.CreatedAt)
            .ToListAsync();
        var left = amount;
        foreach (var lot in lots)
        {
            if (left <= 0)
            {
                break;
            }
            var take = Math.Min(lot.Remaining, left);
            lot.Remaining -= take;
            left -= take;
        }
        db.CashbackTransactions.Add(new CashbackTransaction
        {
            UserId = userId, Amount = -amount, Source = CashbackSources.Redeemed, Description = "Moved into your wallet balance",
            WalletTransactionId = credit.Id, CreatedAt = now,
        });
    }
}

// Earning and expiring cashback. Earned cashback goes into the cashback balance of the user's wallet (offers.cashback),
// where it can be moved into the spendable balance any time (POST /offers/use-cashback). Each credit is one lot with
// an expiry; the unique event key makes the same event pay only once.
public class CashbackService(AppDbContext db, OfferService offers, NotificationService notifications, RewardConfigService config)
{
    // Credits cashback inside the caller's transaction; nothing is saved here. Returns null when amount is 0 or the
    // event already paid.
    public async Task<CashbackTransaction?> CreditAsync(Guid userId, Guid offerId, decimal amount, string source, string description,
        string eventKey, RewardSettings settings, DateTime now, Guid? tripId = null, Guid? campaignId = null, Guid? referralId = null,
        CashbackQuote? breakdown = null, decimal? fare = null, bool notify = true)
    {
        amount = PromotionRules.Floor2(amount);
        if (amount <= 0 || await db.CashbackTransactions.AnyAsync(c => c.EventKey == eventKey))
        {
            return null;
        }
        var reason = source switch
        {
            CashbackSources.FirstTrip => OfferEntryReasons.FirstTrip,
            CashbackSources.Referral => OfferEntryReasons.Referral,
            _ => OfferEntryReasons.Earned,
        };
        var entry = await offers.ChangeCashbackAsync(userId, offerId, amount, reason, description, now);
        var lot = new CashbackTransaction
        {
            UserId = userId, Amount = amount, Remaining = amount, Source = source,
            Description = description.Length > 200 ? description[..200] : description,
            TripId = tripId, CampaignId = campaignId, ReferralId = referralId, EventKey = eventKey,
            ExpiresAt = settings.CashbackExpiryDays > 0 ? now.AddDays(settings.CashbackExpiryDays) : null,
            FareBasis = fare, BaseAmount = breakdown?.BaseAmount, TierBonus = breakdown?.TierBonus, CampaignAmount = breakdown?.CampaignAmount,
            OfferTransactionId = entry?.Id, CreatedAt = now,
        };
        db.CashbackTransactions.Add(lot);
        if (notify)
        {
            var expiry = lot.ExpiresAt is { } at ? $" Move it to your wallet balance before {DhakaTime.FromUtc(at):d MMM yyyy}." : "";
            notifications.Notify(userId, "Cashback credited", $"{PromotionRules.Money(amount)} cashback: {lot.Description}.{expiry}",
                NotificationTypes.Success, "/offers", NotificationCategories.Reward, relatedId: tripId ?? referralId);
        }
        return lot;
    }

    // Cashback a user earned from trips this Dhaka month, which the monthly limit counts.
    public Task<decimal> EarnedThisMonthAsync(Guid userId, DateTime now)
    {
        var start = PromotionRules.MonthStartUtc(now);
        return db.CashbackTransactions.Where(c => c.UserId == userId && c.Source == CashbackSources.Trip && c.CreatedAt >= start)
            .SumAsync(c => c.Amount);
    }

    public async Task<CashbackSummary> SummaryAsync(Guid userId, DateTime now)
    {
        var settings = await config.SettingsAsync();
        var monthStart = PromotionRules.MonthStartUtc(now);
        var soon = now.AddDays(7);
        // One round trip for all of it.
        var row = await db.Users.Where(u => u.Id == userId).Select(u => new
        {
            Balance = db.Offers.Where(o => o.UserId == userId).Select(o => (decimal?)o.Cashback).FirstOrDefault() ?? 0,
            Earned = db.CashbackTransactions.Where(c => c.UserId == userId && c.Amount > 0).Sum(c => (decimal?)c.Amount) ?? 0,
            Used = db.CashbackTransactions.Where(c => c.UserId == userId && c.Source == CashbackSources.Redeemed).Sum(c => (decimal?)c.Amount) ?? 0,
            Expired = db.CashbackTransactions.Where(c => c.UserId == userId && c.Source == CashbackSources.Expired).Sum(c => (decimal?)c.Amount) ?? 0,
            Month = db.CashbackTransactions.Where(c => c.UserId == userId && c.Source == CashbackSources.Trip && c.CreatedAt >= monthStart).Sum(c => (decimal?)c.Amount) ?? 0,
            Soon = db.CashbackTransactions.Where(c => c.UserId == userId && c.Remaining > 0 && c.ExpiresAt != null && c.ExpiresAt <= soon).Sum(c => (decimal?)c.Remaining) ?? 0,
            Next = db.CashbackTransactions.Where(c => c.UserId == userId && c.Remaining > 0 && c.ExpiresAt != null).Min(c => c.ExpiresAt),
            Tier = db.UserMemberships.Where(m => m.UserId == userId).Select(m => m.TierCode).FirstOrDefault(),
        }).AsNoTracking().FirstOrDefaultAsync();
        if (row is null)
        {
            return new CashbackSummary(0, 0, 0, 0, 0, null, 0, null, settings.CashbackExpiryDays);
        }
        var tiers = await config.TiersAsync();
        var tier = tiers.FirstOrDefault(t => t.Code == row.Tier) ?? tiers[0];
        decimal? limit = settings.CashbackMaxPerUserMonthly > 0 ? settings.CashbackMaxPerUserMonthly + tier.ExtraMonthlyCap : null;
        return new CashbackSummary(row.Balance, row.Earned, -row.Used, -row.Expired, row.Month, limit, row.Soon, row.Next, settings.CashbackExpiryDays);
    }

    public async Task<(List<CashbackTransaction> Items, int Total)> HistoryAsync(Guid userId, int page, int pageSize)
    {
        var query = db.CashbackTransactions.Where(c => c.UserId == userId);
        var items = await query.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).AsNoTracking().ToListAsync();
        var total = page == 1 && items.Count < pageSize ? items.Count : await query.CountAsync();
        return (items, total);
    }

    // Expires every lot whose time is up: what is left of it is taken off the cashback balance (never below zero,
    // since some of it may already have been moved to the wallet) and recorded. Returns how many lots expired.
    public async Task<int> ExpireDueAsync(DateTime now, int batch = 50)
    {
        var due = await db.CashbackTransactions.AsNoTracking()
            .Where(c => c.Remaining > 0 && c.ExpiresAt != null && c.ExpiresAt <= now)
            .OrderBy(c => c.ExpiresAt).Take(batch)
            .Select(c => new { c.Id, c.UserId })
            .ToListAsync();
        var expired = 0;
        foreach (var item in due)
        {
            var strategy = db.Database.CreateExecutionStrategy();
            expired += await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                var offer = await offers.EnsureAsync(item.UserId);
                await using var transaction = await db.Database.BeginTransactionAsync();
                // Holding the offer row keeps a cashback move from running between reading and changing the balance.
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM offers WHERE id = {offer.Id} FOR UPDATE");
                var lot = await db.CashbackTransactions.FirstAsync(c => c.Id == item.Id);
                if (lot.Remaining <= 0)
                {
                    await transaction.RollbackAsync();
                    return 0;
                }
                var balance = await db.Offers.Where(o => o.Id == offer.Id).Select(o => o.Cashback).SingleAsync();
                var take = Math.Min(lot.Remaining, balance);
                lot.Remaining = 0;
                lot.ExpiredAt = now;
                if (take > 0)
                {
                    var entry = await offers.ChangeCashbackAsync(item.UserId, offer.Id, -take, OfferEntryReasons.Expired, $"Cashback expired: {lot.Description}", now);
                    db.CashbackTransactions.Add(new CashbackTransaction
                    {
                        UserId = item.UserId, Amount = -take, Source = CashbackSources.Expired,
                        Description = lot.Description.Length > 191 ? $"Expired: {lot.Description[..191]}" : $"Expired: {lot.Description}",
                        OfferTransactionId = entry?.Id, CreatedAt = now,
                    });
                }
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
                return 1;
            });
        }
        return expired;
    }
}
