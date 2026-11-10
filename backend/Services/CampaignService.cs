using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

// A live campaign as a user sees it, with whether they qualify.
public record CampaignView(RewardCampaign Campaign, List<string> Conditions, bool? Eligible, string? Note, decimal? BudgetLeftPercent);

public class CampaignService(AppDbContext db, RewardConfigService config)
{
    // Campaigns running now (active, started, not ended, budget left). userId, when signed in, adds whether that
    // user qualifies.
    public async Task<List<CampaignView>> ActiveAsync(Guid? userId, DateTime now)
    {
        var tiers = await config.TiersAsync();
        var settings = await config.SettingsAsync();
        var campaigns = await db.RewardCampaigns.AsNoTracking()
            .Where(c => c.Status == CampaignStatus.Active && c.StartsAt <= now && c.EndsAt > now
                && (c.Budget == null || c.Spent < c.Budget) && (c.MaxRedemptions == null || c.Redemptions < c.MaxRedemptions))
            .OrderBy(c => c.EndsAt).ToListAsync();
        var me = userId is { } id
            ? await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => new
            {
                u.CreatedAt,
                Tier = db.UserMemberships.Where(m => m.UserId == id).Select(m => m.TierCode).FirstOrDefault(),
                Trips = db.TripRewards.Count(r => r.UserId == id && r.Eligible),
            }).FirstOrDefaultAsync()
            : null;
        return campaigns.Select(c =>
        {
            bool? eligible = null;
            string? note = null;
            if (me is not null)
            {
                var rank = PromotionRules.RankOf(tiers, me.Tier);
                var isNew = PromotionRules.IsNewUser(me.CreatedAt, now, settings, me.Trips);
                note = c.MinTier is { } t && rank < PromotionRules.RankOf(tiers, t) ? $"For {PromotionRules.NameOf(tiers, t)} members and above."
                    : c.NewUsersOnly && !isNew ? "For new users only."
                    : c.FirstTripOnly && me.Trips > 0 ? "For a first eligible trip only."
                    : null;
                eligible = note is null;
            }
            decimal? left = c.Budget is { } budget && budget > 0 ? Math.Round(Math.Max(0, budget - c.Spent) / budget * 100, 0) : null;
            return new CampaignView(c, Conditions(c, tiers), eligible, note, left);
        }).ToList();
    }

    public static List<string> Conditions(RewardCampaign c, IReadOnlyList<MembershipTier> tiers)
    {
        var list = new List<string>();
        if (c.MinFare > 0) list.Add($"Fare of at least {PromotionRules.Money(c.MinFare)}");
        if (c.WeekendOnly) list.Add("Friday and Saturday trips");
        if (c.NewUsersOnly) list.Add("New users");
        if (c.FirstTripOnly) list.Add("Your first eligible trip");
        if (c.MinTier is { } tier) list.Add($"{PromotionRules.NameOf(tiers, tier)} members and above");
        if (c.MaxCashbackPerTrip is { } max) list.Add($"Up to {PromotionRules.Money(max)} per trip");
        list.Add("Completed Dhaka bus trips (boarded and ended in the app)");
        return list;
    }

    public static string RewardText(RewardCampaign c)
    {
        var parts = new List<string>();
        if (c.CashbackPercent > 0) parts.Add($"{c.CashbackPercent:0.##}% cashback");
        if (c.CashbackFixed > 0) parts.Add($"{PromotionRules.Money(c.CashbackFixed)} cashback");
        return parts.Count > 0 ? "Extra " + string.Join(" + ", parts) + " per trip" : "Exclusive coupons";
    }
}
