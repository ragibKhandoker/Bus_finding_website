using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

// The reward settings and membership tiers. They change rarely (only when an admin saves them), so they are kept
// in memory for a minute; saving through this service clears that copy at once.
public class RewardConfigService(AppDbContext db, IOptions<PromotionOptions> options, IMemoryCache cache)
{
    private const string SettingsKey = "rewards:settings";
    private const string TiersKey = "rewards:tiers";
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);

    public PromotionOptions Defaults => options.Value;

    public static readonly TierOption[] BuiltInTiers =
    [
        new() { Code = "bronze", Name = "Bronze", MinTrips = 0, Benefits = "Standard cashback on every eligible trip." },
        new() { Code = "silver", Name = "Silver", MinTrips = 10, ExtraCashbackPercent = 1, ExtraCapPerTrip = 2, ExtraMonthlyCap = 50, Benefits = "+1% cashback and a higher cashback limit." },
        new() { Code = "gold", Name = "Gold", MinTrips = 25, ExtraCashbackPercent = 2, ExtraCapPerTrip = 5, ExtraMonthlyCap = 100, Benefits = "+2% cashback, higher limits and Gold-only coupons." },
        new() { Code = "platinum", Name = "Platinum", MinTrips = 50, ExtraCashbackPercent = 3, ExtraCapPerTrip = 10, ExtraMonthlyCap = 200, Benefits = "+3% cashback, the highest limits and Platinum-only offers." },
    ];

    public RewardSettings NewDefaultSettings(DateTime nowUtc)
    {
        var d = options.Value;
        return new RewardSettings
        {
            Id = 1,
            CashbackEnabled = d.CashbackEnabled,
            CashbackPercent = d.CashbackPercent,
            CashbackFixed = d.CashbackFixed,
            CashbackMinFare = d.CashbackMinFare,
            CashbackMaxPerTrip = d.CashbackMaxPerTrip,
            CashbackMaxPerUserMonthly = d.CashbackMaxPerUserMonthly,
            CashbackExpiryDays = d.CashbackExpiryDays,
            AllowCouponStacking = d.AllowCouponStacking,
            AllowCouponWithCashback = d.AllowCouponWithCashback,
            NewUserDays = d.NewUserDays,
            FirstTripCashback = d.FirstTripCashback,
            ReferralEnabled = d.ReferralEnabled,
            ReferrerReward = d.ReferrerReward,
            ReferredReward = d.ReferredReward,
            ReferralApplyDays = d.ReferralApplyDays,
            MaxReferralRewardsPerMonth = d.MaxReferralRewardsPerMonth,
            StartedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
    }

    // The settings, created from appsettings.json the first time. Never tracked: change them with SaveSettingsAsync.
    public async Task<RewardSettings> SettingsAsync()
    {
        if (cache.TryGetValue(SettingsKey, out RewardSettings? cached) && cached is not null)
        {
            return cached;
        }
        var settings = await db.RewardSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1);
        if (settings is null)
        {
            await EnsureDefaultsAsync();
            settings = await db.RewardSettings.AsNoTracking().FirstAsync(s => s.Id == 1);
        }
        cache.Set(SettingsKey, settings, CacheFor);
        return settings;
    }

    // Lowest tier first. Never empty: the built-in tiers are created when there are none.
    public async Task<List<MembershipTier>> TiersAsync()
    {
        if (cache.TryGetValue(TiersKey, out List<MembershipTier>? cached) && cached is { Count: > 0 })
        {
            return cached;
        }
        var tiers = await db.MembershipTiers.AsNoTracking().OrderBy(t => t.Rank).ToListAsync();
        if (tiers.Count == 0)
        {
            await EnsureDefaultsAsync();
            tiers = await db.MembershipTiers.AsNoTracking().OrderBy(t => t.Rank).ToListAsync();
        }
        cache.Set(TiersKey, tiers, CacheFor);
        return tiers;
    }

    // Puts known settings and tiers in the memory copy (used by tests that run without a database).
    public void Remember(RewardSettings settings, List<MembershipTier> tiers)
    {
        cache.Set(SettingsKey, settings, CacheFor);
        cache.Set(TiersKey, PromotionRules.Ordered(tiers), CacheFor);
    }

    public void Forget()
    {
        cache.Remove(SettingsKey);
        cache.Remove(TiersKey);
    }

    // Creates the settings row and the tiers if they are missing. Safe to run at every start and from two servers
    // at once: a row another request created first is left alone.
    public async Task EnsureDefaultsAsync()
    {
        var now = DateTime.UtcNow;
        if (!await db.RewardSettings.AnyAsync())
        {
            db.RewardSettings.Add(NewDefaultSettings(now));
            await SaveIgnoringDuplicatesAsync();
        }
        if (!await db.MembershipTiers.AnyAsync())
        {
            var tiers = options.Value.Tiers is { Count: > 0 } configured ? configured.ToArray() : BuiltInTiers;
            var rank = 0;
            foreach (var t in tiers.Where(t => !string.IsNullOrWhiteSpace(t.Code)).OrderBy(t => t.MinTrips))
            {
                db.MembershipTiers.Add(new MembershipTier
                {
                    Code = t.Code.Trim().ToLowerInvariant(), Name = t.Name.Trim(), Rank = rank++, MinTrips = Math.Max(0, t.MinTrips),
                    MinSpend = Math.Max(0, t.MinSpend), ExtraCashbackPercent = t.ExtraCashbackPercent, ExtraCapPerTrip = t.ExtraCapPerTrip,
                    ExtraMonthlyCap = t.ExtraMonthlyCap, Benefits = t.Benefits, UpdatedAt = now,
                });
            }
            await SaveIgnoringDuplicatesAsync();
        }
        Forget();
    }

    private async Task SaveIgnoringDuplicatesAsync()
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
        }
    }

    // Development only: example coupons and a campaign so the Offers page has something to show. Each is added
    // only when nothing with its code (or name) exists, so running this again changes nothing.
    public async Task SeedDemoOffersAsync()
    {
        var now = DateTime.UtcNow;
        var campaignName = "Weekend Cashback Festival";
        var campaign = await db.RewardCampaigns.FirstOrDefaultAsync(c => c.Name == campaignName);
        if (campaign is null)
        {
            campaign = new RewardCampaign
            {
                Name = campaignName,
                Description = "Extra 5% cashback on Friday and Saturday bus trips across Dhaka.",
                Type = CampaignTypes.Weekend, Theme = "sunset", StartsAt = now.AddMinutes(-1), EndsAt = now.AddDays(60),
                CashbackPercent = 5, MaxCashbackPerTrip = 10, MinFare = 20, WeekendOnly = true, Budget = 20_000,
            };
            db.RewardCampaigns.Add(campaign);
            db.RewardAuditLogs.Add(new RewardAuditLog { Action = "seed:campaign", EntityType = "campaign", Details = campaignName });
        }
        var demo = new[]
        {
            new Coupon { Code = "DHAKA10", Title = "10% off your bus fare", Description = "Save 10% on any completed Dhaka bus trip.",
                Type = CouponTypes.Percentage, DiscountType = PromoDiscountTypes.Percent, DiscountValue = 10, MinFare = 50, MaxDiscount = 20,
                PerUserLimit = 1, TotalLimit = 1000, ExpiresAt = now.AddDays(90) },
            new Coupon { Code = "WELCOME15", Title = "Welcome to SmartCommute", Description = "৳15 off a trip for new accounts.",
                Type = CouponTypes.Welcome, DiscountType = PromoDiscountTypes.Fixed, DiscountValue = 15, MinFare = 20,
                Audience = CouponAudiences.Assigned, ValidDaysAfterIssue = 30, PerUserLimit = 1 },
            new Coupon { Code = "FIRSTRIDE", Title = "Thanks for your first ride", Description = "20% off your next trip after your first eligible trip.",
                Type = CouponTypes.FirstTrip, DiscountType = PromoDiscountTypes.Percent, DiscountValue = 20, MaxDiscount = 15,
                Audience = CouponAudiences.Assigned, ValidDaysAfterIssue = 30, PerUserLimit = 1 },
            new Coupon { Code = "FRIEND10", Title = "Referral reward", Description = "৳10 off for you and the friend you invited.",
                Type = CouponTypes.Referral, DiscountType = PromoDiscountTypes.Fixed, DiscountValue = 10, MinFare = 20,
                Audience = CouponAudiences.Assigned, ValidDaysAfterIssue = 60, PerUserLimit = 1 },
            new Coupon { Code = "WEEKEND15", Title = "Weekend saver", Description = "15% off trips on Friday and Saturday.",
                Type = CouponTypes.Weekend, DiscountType = PromoDiscountTypes.Percent, DiscountValue = 15, MinFare = 30, MaxDiscount = 25,
                WeekendOnly = true, PerUserLimit = 4, ExpiresAt = now.AddDays(60), Campaign = campaign },
            new Coupon { Code = "GOLD25", Title = "Gold member exclusive", Description = "৳25 off for Gold and Platinum members.",
                Type = CouponTypes.Campaign, DiscountType = PromoDiscountTypes.Fixed, DiscountValue = 25, MinFare = 40,
                MinTier = "gold", PerUserLimit = 2, ExpiresAt = now.AddDays(120) },
        };
        var codes = demo.Select(c => c.Code).ToList();
        var existing = await db.Coupons.Where(c => codes.Contains(c.Code)).Select(c => c.Code).ToListAsync();
        foreach (var coupon in demo.Where(c => !existing.Contains(c.Code)))
        {
            coupon.StartsAt = now.AddMinutes(-1);
            db.Coupons.Add(coupon);
        }
        await SaveIgnoringDuplicatesAsync();
    }
}
