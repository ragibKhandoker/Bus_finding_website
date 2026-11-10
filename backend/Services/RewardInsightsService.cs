using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

public record MonthSavings(string Month, string Label, decimal Cashback, decimal Discounts);

public record RewardEvent(string Kind, string Title, string Detail, decimal Amount, DateTime At);

public record SavingsAnalytics(decimal TotalCashback, decimal TotalDiscounts, decimal TotalSaved, int CouponsUsed, string? TopCoupon, int TopCouponUses,
    List<MonthSavings> Months, List<RewardEvent> History);

public record RewardsSummary(decimal AvailableCashback, decimal TotalCashbackEarned, int AvailableCoupons, decimal TotalSaved, string TierCode,
    string TierName, int EligibleTrips, decimal WalletBalance);

// The savings numbers on the Offers page. Everything is counted from the ledgers; nothing is estimated.
public class RewardInsightsService(AppDbContext db, CouponService coupons, MembershipService membership)
{
    public async Task<SavingsAnalytics> AnalyticsAsync(Guid userId, DateTime now, int months = 6)
    {
        var local = DhakaTime.FromUtc(now);
        var firstMonth = new DateTime(local.Year, local.Month, 1).AddMonths(-(months - 1));
        var since = DhakaTime.ToUtc(firstMonth);

        var earned = await db.CashbackTransactions.AsNoTracking()
            .Where(c => c.UserId == userId && c.Amount > 0)
            .OrderByDescending(c => c.CreatedAt).Take(500)
            .Select(c => new { c.Amount, c.CreatedAt, c.Source, c.Description })
            .ToListAsync();
        var redeemed = await db.CouponRedemptions.AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt).Take(500)
            .Select(r => new { r.Discount, r.CreatedAt, r.Coupon!.Code, Trip = r.Trip!.Source + " to " + r.Trip.Destination })
            .ToListAsync();
        // Coupon and discount credits used on trips before coupon codes existed (the older offer balances).
        var credits = await db.OfferTransactions.AsNoTracking()
            .Where(x => x.UserId == userId && x.Reason == OfferEntryReasons.Trip)
            .OrderByDescending(x => x.CreatedAt).Take(500)
            .Select(x => new { x.Amount, x.CreatedAt, x.Kind, x.Description })
            .ToListAsync();

        string Key(DateTime utc) => DhakaTime.FromUtc(utc).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var list = new List<MonthSavings>();
        for (var i = 0; i < months; i++)
        {
            var month = firstMonth.AddMonths(i);
            var key = month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            list.Add(new MonthSavings(key, month.ToString("MMM", CultureInfo.InvariantCulture),
                earned.Where(e => e.CreatedAt >= since && Key(e.CreatedAt) == key).Sum(e => e.Amount),
                redeemed.Where(r => r.CreatedAt >= since && Key(r.CreatedAt) == key).Sum(r => r.Discount)
                    - credits.Where(c => c.CreatedAt >= since && Key(c.CreatedAt) == key).Sum(c => c.Amount)));
        }
        var top = redeemed.GroupBy(r => r.Code).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Max(r => r.CreatedAt)).FirstOrDefault();
        var history = earned.Select(e => new RewardEvent("cashback", e.Source switch
            {
                CashbackSources.FirstTrip => "First trip bonus",
                CashbackSources.Referral => "Referral reward",
                _ => "Cashback earned",
            }, e.Description, e.Amount, e.CreatedAt))
            .Concat(redeemed.Select(r => new RewardEvent("coupon", $"Coupon {r.Code}", r.Trip, r.Discount, r.CreatedAt)))
            .Concat(credits.Select(c => new RewardEvent("credit", c.Kind == OfferKinds.Coupon ? "Coupon credit used" : "Discount credit used", c.Description, -c.Amount, c.CreatedAt)))
            .OrderByDescending(e => e.At).Take(15).ToList();
        var totalCashback = earned.Sum(e => e.Amount);
        var totalDiscounts = redeemed.Sum(r => r.Discount) - credits.Sum(c => c.Amount);
        return new SavingsAnalytics(totalCashback, totalDiscounts, totalCashback + totalDiscounts, redeemed.Count, top?.Key, top?.Count() ?? 0, list, history);
    }

    public async Task<RewardsSummary> SummaryAsync(Guid userId, DateTime now)
    {
        var row = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new
        {
            Cashback = db.Offers.Where(o => o.UserId == userId).Select(o => (decimal?)o.Cashback).FirstOrDefault() ?? 0,
            Earned = db.CashbackTransactions.Where(c => c.UserId == userId && c.Amount > 0).Sum(c => (decimal?)c.Amount) ?? 0,
            Coupons = db.CouponRedemptions.Where(r => r.UserId == userId).Sum(r => (decimal?)r.Discount) ?? 0,
            Credits = db.OfferTransactions.Where(x => x.UserId == userId && x.Reason == OfferEntryReasons.Trip).Sum(x => (decimal?)x.Amount) ?? 0,
            Wallet = db.Wallets.Where(w => w.UserId == userId).Select(w => (decimal?)w.Balance).FirstOrDefault() ?? 0,
        }).FirstAsync();
        var lists = await coupons.ListAsync(userId, now);
        var tier = await membership.MyAsync(userId);
        return new RewardsSummary(row.Cashback, row.Earned, lists.Available.Count(i => i.Status == "available"),
            row.Earned + row.Coupons - row.Credits, tier.Tier.Code, tier.Tier.Name, tier.EligibleTrips, row.Wallet);
    }
}
