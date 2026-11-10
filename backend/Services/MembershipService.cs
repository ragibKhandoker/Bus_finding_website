using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

public record MembershipView(MembershipTier Tier, MembershipTier? Next, int EligibleTrips, decimal EligibleSpend, int TripsToNext,
    decimal SpendToNext, int Percent, DateTime? UpgradedAt, List<MembershipTier> Tiers, decimal CashbackPercent);

// The user's loyalty tier. RewardEngine moves it forward as eligible trips are completed; this only reads it.
public class MembershipService(AppDbContext db, RewardConfigService config)
{
    public async Task<MembershipView> MyAsync(Guid userId)
    {
        var tiers = await config.TiersAsync();
        var settings = await config.SettingsAsync();
        var row = await db.UserMemberships.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == userId);
        int trips;
        decimal spend;
        if (row is null)
        {
            // No eligible trip processed yet (or the row was never made): counted from the trip rewards.
            var counts = await db.TripRewards.Where(r => r.UserId == userId && r.Eligible)
                .GroupBy(r => 1).Select(g => new { Trips = g.Count(), Spend = g.Sum(r => r.Fare) }).FirstOrDefaultAsync();
            trips = counts?.Trips ?? 0;
            spend = counts?.Spend ?? 0;
        }
        else
        {
            trips = row.EligibleTrips;
            spend = row.EligibleSpend;
        }
        var progress = PromotionRules.Progress(tiers, trips, spend);
        // A tier is never taken away: an admin raising a threshold keeps people where they already are.
        var current = row is not null && tiers.FirstOrDefault(t => t.Code == row.TierCode) is { } kept && kept.Rank > progress.Current.Rank
            ? kept : progress.Current;
        var next = tiers.FirstOrDefault(t => t.Rank > current.Rank);
        var view = current == progress.Current ? progress : PromotionRules.Progress(tiers.Where(t => t.Rank >= current.Rank).ToList(), trips, spend);
        return new MembershipView(current, next, trips, spend, next is null ? 0 : Math.Max(0, next.MinTrips - trips),
            next is null || next.MinSpend <= 0 ? 0 : Math.Max(0, next.MinSpend - spend), next is null ? 100 : view.Percent, row?.UpgradedAt,
            tiers, settings.CashbackPercent + current.ExtraCashbackPercent);
    }
}
