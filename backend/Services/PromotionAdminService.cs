using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

public record PromoResult<T>(int StatusCode, string Message, T? Value = default)
{
    public bool Succeeded => StatusCode is >= 200 and < 300;
}

public record SuspiciousItem(string Kind, Guid UserId, string Name, string Email, int Count, string Detail);

// The admin's side of F15: coupons, campaigns, the reward settings and tiers, statistics, and the audit log. Every
// change an admin makes is written to reward_audit_logs in the same save.
public class PromotionAdminService(AppDbContext db, RewardConfigService config, CouponService coupons, NotificationService notifications)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly string[] Themes = ["green", "sunset", "eid", "night", "sky", "gold"];

    private void Audit(Guid adminId, string action, string type, Guid? id, object details) => db.RewardAuditLogs.Add(new RewardAuditLog
    {
        ActorId = adminId, Action = action, EntityType = type, EntityId = id?.ToString(),
        Details = Clip(JsonSerializer.Serialize(details, Json), 1000), CreatedAt = DateTime.UtcNow,
    });

    private static string Clip(string text, int max) => text.Length > max ? text[..max] : text;

    // ---- Coupons ----

    public async Task<List<Coupon>> CouponsAsync(bool includeDeleted = false) => await db.Coupons.Include(c => c.Campaign).AsNoTracking()
        .Where(c => includeDeleted || c.DeletedAt == null).OrderByDescending(c => c.CreatedAt).ToListAsync();

    private async Task<string?> CouponProblemAsync(CouponRequest r, Coupon? existing, DateTime now)
    {
        if (PromotionRules.NormalizeCode(r.Code) is not { } code)
        {
            return "The code must be 3 to 30 letters, numbers, - or _ (for example DHAKA10).";
        }
        if (!CouponTypes.All.Contains(r.Type))
        {
            return $"Type must be one of: {string.Join(", ", CouponTypes.All)}.";
        }
        if (!PromoDiscountTypes.All.Contains(r.DiscountType))
        {
            return "Discount type must be fixed or percent.";
        }
        if (r.DiscountType == PromoDiscountTypes.Percent && r.DiscountValue > 100)
        {
            return "A percentage discount cannot be more than 100%.";
        }
        if (decimal.Round(r.DiscountValue, 2) != r.DiscountValue || r.MaxDiscount is { } m && decimal.Round(m, 2) != m || decimal.Round(r.MinFare, 2) != r.MinFare)
        {
            return "Amounts can have at most 2 decimal places.";
        }
        if (!CouponAudiences.All.Contains(r.Audience))
        {
            return "Audience must be all, new-users or assigned.";
        }
        var starts = string.IsNullOrWhiteSpace(r.StartsAt) ? (DateTime?)null : DhakaTime.ParseToUtc(r.StartsAt);
        var expires = string.IsNullOrWhiteSpace(r.ExpiresAt) ? (DateTime?)null : DhakaTime.ParseToUtc(r.ExpiresAt);
        if (!string.IsNullOrWhiteSpace(r.StartsAt) && starts is null || !string.IsNullOrWhiteSpace(r.ExpiresAt) && expires is null)
        {
            return "Enter dates like 2026-12-31T23:59.";
        }
        if (expires is { } e && e <= (starts ?? existing?.StartsAt ?? now))
        {
            return "The expiry must be after the start.";
        }
        if (existing is not null && r.TotalLimit is { } limit && limit < existing.RedemptionCount)
        {
            return $"The total limit cannot be below the {existing.RedemptionCount} redemptions already made.";
        }
        if (r.MinTier is { Length: > 0 } tier && (await config.TiersAsync()).All(t => t.Code != tier))
        {
            return "That membership tier does not exist.";
        }
        if (r.CampaignId is { } campaignId && !await db.RewardCampaigns.AnyAsync(c => c.Id == campaignId))
        {
            return "That campaign does not exist.";
        }
        if (r.RouteNumber is { Length: > 0 } route && !await db.Buses.AnyAsync(b => b.RouteNumber == route.Trim()))
        {
            return $"There is no Dhaka route numbered {route.Trim()}.";
        }
        if (await db.Coupons.AnyAsync(c => c.Code == code && (existing == null || c.Id != existing.Id)))
        {
            return $"The code {code} is already used by another coupon.";
        }
        return null;
    }

    private static void Apply(Coupon c, CouponRequest r, DateTime now)
    {
        c.Code = PromotionRules.NormalizeCode(r.Code)!;
        c.Title = r.Title.Trim();
        c.Description = r.Description?.Trim() ?? "";
        c.Type = r.Type;
        c.DiscountType = r.DiscountType;
        c.DiscountValue = r.DiscountValue;
        c.MinFare = r.MinFare;
        c.MaxDiscount = r.MaxDiscount;
        if (DhakaTime.ParseToUtc(r.StartsAt) is { } starts) c.StartsAt = starts;
        c.ExpiresAt = DhakaTime.ParseToUtc(r.ExpiresAt);
        c.TotalLimit = r.TotalLimit;
        c.PerUserLimit = r.PerUserLimit;
        c.CampaignId = r.CampaignId;
        c.IsActive = r.IsActive;
        // Welcome, first-trip and referral coupons are handed to people, never shown to everyone.
        c.Audience = CouponTypes.Issued.Contains(r.Type) ? CouponAudiences.Assigned : r.Audience;
        c.MinTier = string.IsNullOrWhiteSpace(r.MinTier) ? null : r.MinTier.Trim();
        c.RouteNumber = string.IsNullOrWhiteSpace(r.RouteNumber) ? null : r.RouteNumber.Trim();
        c.WeekendOnly = r.WeekendOnly || r.Type == CouponTypes.Weekend;
        c.ValidDaysAfterIssue = r.ValidDaysAfterIssue;
        c.UpdatedAt = now;
    }

    public async Task<PromoResult<Coupon>> CreateCouponAsync(Guid adminId, CouponRequest r)
    {
        var now = DateTime.UtcNow;
        if (await CouponProblemAsync(r, null, now) is { } problem)
        {
            return new(problem.Contains("already used") ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest, problem);
        }
        var coupon = new Coupon { StartsAt = now, CreatedById = adminId, CreatedAt = now };
        Apply(coupon, r, now);
        db.Coupons.Add(coupon);
        Audit(adminId, "coupon:create", "coupon", coupon.Id, r);
        return await SaveCouponAsync(coupon, $"Coupon {coupon.Code} created.", StatusCodes.Status201Created);
    }

    public async Task<PromoResult<Coupon>> UpdateCouponAsync(Guid adminId, Guid id, CouponRequest r)
    {
        var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null);
        if (coupon is null)
        {
            return new(StatusCodes.Status404NotFound, "Coupon not found.");
        }
        var now = DateTime.UtcNow;
        if (await CouponProblemAsync(r, coupon, now) is { } problem)
        {
            return new(problem.Contains("already used") ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest, problem);
        }
        var before = new { coupon.Code, coupon.DiscountType, coupon.DiscountValue, coupon.IsActive, coupon.TotalLimit, coupon.ExpiresAt };
        Apply(coupon, r, now);
        Audit(adminId, "coupon:update", "coupon", coupon.Id, new { before, after = r });
        return await SaveCouponAsync(coupon, $"Coupon {coupon.Code} saved.", StatusCodes.Status200OK);
    }

    private async Task<PromoResult<Coupon>> SaveCouponAsync(Coupon coupon, string message, int status)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return new(StatusCodes.Status409Conflict, $"The code {coupon.Code} is already used by another coupon.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.CheckViolation })
        {
            db.ChangeTracker.Clear();
            return new(StatusCodes.Status400BadRequest, "Some values are out of range. Check the discount, limits and dates.");
        }
        var saved = await db.Coupons.Include(c => c.Campaign).AsNoTracking().FirstAsync(c => c.Id == coupon.Id);
        return new(status, message, saved);
    }

    // Deleting keeps the coupon (its redemptions point to it) but switches it off and hides it for good.
    public async Task<PromoResult<bool>> DeleteCouponAsync(Guid adminId, Guid id)
    {
        var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null);
        if (coupon is null)
        {
            return new(StatusCodes.Status404NotFound, "Coupon not found.");
        }
        coupon.DeletedAt = DateTime.UtcNow;
        coupon.IsActive = false;
        Audit(adminId, "coupon:delete", "coupon", coupon.Id, new { coupon.Code, coupon.RedemptionCount });
        await db.SaveChangesAsync();
        return new(StatusCodes.Status200OK, $"Coupon {coupon.Code} deleted. Its {coupon.RedemptionCount} redemptions stay in the history.", true);
    }

    public async Task<PromoResult<UserCoupon>> IssueCouponAsync(Guid adminId, Guid couponId, string email)
    {
        var coupon = await db.Coupons.AsNoTracking().FirstOrDefaultAsync(c => c.Id == couponId && c.DeletedAt == null);
        if (coupon is null)
        {
            return new(StatusCodes.Status404NotFound, "Coupon not found.");
        }
        var address = email.Trim().ToLowerInvariant();
        var user = await db.Users.AsNoTracking().Where(u => u.Email == address).Select(u => new { u.Id, u.Name }).FirstOrDefaultAsync();
        if (user is null)
        {
            return new(StatusCodes.Status404NotFound, "No account uses that email address.");
        }
        var now = DateTime.UtcNow;
        var copy = await coupons.IssueAsync(user.Id, coupon, "admin", $"admin:{coupon.Id}", now);
        if (copy is null)
        {
            return new(StatusCodes.Status409Conflict, $"{user.Name} already has this coupon.");
        }
        notifications.Notify(user.Id, "New coupon", $"You received the coupon {coupon.Code}: {coupon.Title}. Use it on a completed bus trip from the Offers page.",
            NotificationTypes.Success, "/offers", NotificationCategories.Reward);
        Audit(adminId, "coupon:issue", "coupon", coupon.Id, new { coupon.Code, userId = user.Id });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return new(StatusCodes.Status409Conflict, $"{user.Name} already has this coupon.");
        }
        return new(StatusCodes.Status201Created, $"{coupon.Code} issued to {user.Name}.", copy);
    }

    // ---- Campaigns ----

    public Task<List<RewardCampaign>> CampaignsAsync() => db.RewardCampaigns.AsNoTracking().OrderByDescending(c => c.CreatedAt).ToListAsync();

    private async Task<string?> CampaignProblemAsync(CampaignRequest r, RewardCampaign? existing)
    {
        if (!CampaignTypes.All.Contains(r.Type))
        {
            return $"Type must be one of: {string.Join(", ", CampaignTypes.All)}.";
        }
        if (r.Theme is { Length: > 0 } theme && !Themes.Contains(theme))
        {
            return $"Theme must be one of: {string.Join(", ", Themes)}.";
        }
        if (r.BannerUrl is { Length: > 0 } url && !(Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps))
        {
            return "The banner must be an https:// image link, or left empty to use the drawn artwork.";
        }
        var starts = DhakaTime.ParseToUtc(r.StartsAt);
        var ends = DhakaTime.ParseToUtc(r.EndsAt);
        if (starts is null || ends is null)
        {
            return "Enter the start and end like 2026-12-31T23:59.";
        }
        if (ends <= starts)
        {
            return "The campaign must end after it starts.";
        }
        if (r.Status is { Length: > 0 } status && !CampaignStatus.All.Contains(status))
        {
            return "Status must be active, paused or expired.";
        }
        if (r.MinTier is { Length: > 0 } tier && (await config.TiersAsync()).All(t => t.Code != tier))
        {
            return "That membership tier does not exist.";
        }
        if (existing is not null && r.Budget is { } budget && budget < existing.Spent)
        {
            return $"The budget cannot be below the {PromotionRules.Money(existing.Spent)} already given out.";
        }
        if (existing is not null && r.MaxRedemptions is { } max && max < existing.Redemptions)
        {
            return $"The reward limit cannot be below the {existing.Redemptions} rewards already given.";
        }
        return null;
    }

    private static void Apply(RewardCampaign c, CampaignRequest r, DateTime now)
    {
        c.Name = r.Name.Trim();
        c.Description = r.Description?.Trim() ?? "";
        c.Type = r.Type;
        c.BannerUrl = string.IsNullOrWhiteSpace(r.BannerUrl) ? null : r.BannerUrl.Trim();
        c.Theme = string.IsNullOrWhiteSpace(r.Theme) ? "green" : r.Theme;
        c.StartsAt = DhakaTime.ParseToUtc(r.StartsAt)!.Value;
        c.EndsAt = DhakaTime.ParseToUtc(r.EndsAt)!.Value;
        c.CashbackPercent = r.CashbackPercent;
        c.CashbackFixed = r.CashbackFixed;
        c.MaxCashbackPerTrip = r.MaxCashbackPerTrip;
        c.MinFare = r.MinFare;
        c.MinTier = string.IsNullOrWhiteSpace(r.MinTier) ? null : r.MinTier.Trim();
        c.NewUsersOnly = r.NewUsersOnly || r.Type == CampaignTypes.Welcome;
        c.WeekendOnly = r.WeekendOnly || r.Type == CampaignTypes.Weekend;
        c.FirstTripOnly = r.FirstTripOnly || r.Type == CampaignTypes.FirstTrip;
        c.Budget = r.Budget;
        c.MaxRedemptions = r.MaxRedemptions;
        if (r.Status is { Length: > 0 } status) c.Status = status;
        if (c.Status == CampaignStatus.Active && c.EndsAt <= now) c.Status = CampaignStatus.Expired;
        c.UpdatedAt = now;
    }

    public async Task<PromoResult<RewardCampaign>> CreateCampaignAsync(Guid adminId, CampaignRequest r)
    {
        if (await CampaignProblemAsync(r, null) is { } problem)
        {
            return new(StatusCodes.Status400BadRequest, problem);
        }
        var now = DateTime.UtcNow;
        var campaign = new RewardCampaign { CreatedById = adminId, CreatedAt = now };
        Apply(campaign, r, now);
        db.RewardCampaigns.Add(campaign);
        Audit(adminId, "campaign:create", "campaign", campaign.Id, r);
        await db.SaveChangesAsync();
        return new(StatusCodes.Status201Created, $"Campaign \"{campaign.Name}\" created.", campaign);
    }

    public async Task<PromoResult<RewardCampaign>> UpdateCampaignAsync(Guid adminId, Guid id, CampaignRequest r)
    {
        var campaign = await db.RewardCampaigns.FirstOrDefaultAsync(c => c.Id == id);
        if (campaign is null)
        {
            return new(StatusCodes.Status404NotFound, "Campaign not found.");
        }
        if (await CampaignProblemAsync(r, campaign) is { } problem)
        {
            return new(StatusCodes.Status400BadRequest, problem);
        }
        Apply(campaign, r, DateTime.UtcNow);
        Audit(adminId, "campaign:update", "campaign", campaign.Id, r);
        await db.SaveChangesAsync();
        return new(StatusCodes.Status200OK, $"Campaign \"{campaign.Name}\" saved.", campaign);
    }

    // Pausing stops a campaign from giving new rewards straight away; expiring ends it for good.
    public async Task<PromoResult<RewardCampaign>> SetCampaignStatusAsync(Guid adminId, Guid id, string status)
    {
        status = status.Trim().ToLowerInvariant();
        if (!CampaignStatus.All.Contains(status))
        {
            return new(StatusCodes.Status400BadRequest, "Status must be active, paused or expired.");
        }
        var campaign = await db.RewardCampaigns.FirstOrDefaultAsync(c => c.Id == id);
        if (campaign is null)
        {
            return new(StatusCodes.Status404NotFound, "Campaign not found.");
        }
        var now = DateTime.UtcNow;
        if (status == CampaignStatus.Active && campaign.EndsAt <= now)
        {
            return new(StatusCodes.Status409Conflict, "This campaign's end date has passed. Change the end date to run it again.");
        }
        var before = campaign.Status;
        campaign.Status = status;
        campaign.UpdatedAt = now;
        Audit(adminId, $"campaign:{status}", "campaign", campaign.Id, new { campaign.Name, before, after = status });
        await db.SaveChangesAsync();
        var verb = status == CampaignStatus.Active ? "is running" : status == CampaignStatus.Paused ? "is paused" : "has ended";
        return new(StatusCodes.Status200OK, $"\"{campaign.Name}\" {verb}.", campaign);
    }

    // ---- Settings and tiers ----

    public async Task<PromoResult<RewardSettings>> SaveSettingsAsync(Guid adminId, RewardSettingsRequest r)
    {
        foreach (var amount in new[] { r.CashbackPercent, r.CashbackFixed, r.CashbackMinFare, r.CashbackMaxPerTrip, r.CashbackMaxPerUserMonthly, r.FirstTripCashback, r.ReferrerReward, r.ReferredReward })
        {
            if (decimal.Round(amount, 2) != amount)
            {
                return new(StatusCodes.Status400BadRequest, "Amounts can have at most 2 decimal places.");
            }
        }
        await config.SettingsAsync(); // makes sure the row exists
        var settings = await db.RewardSettings.FirstAsync(s => s.Id == 1);
        var before = RewardSettingsViewModel.From(settings);
        settings.CashbackEnabled = r.CashbackEnabled;
        settings.CashbackPercent = r.CashbackPercent;
        settings.CashbackFixed = r.CashbackFixed;
        settings.CashbackMinFare = r.CashbackMinFare;
        settings.CashbackMaxPerTrip = r.CashbackMaxPerTrip;
        settings.CashbackMaxPerUserMonthly = r.CashbackMaxPerUserMonthly;
        settings.CashbackExpiryDays = r.CashbackExpiryDays;
        settings.AllowCouponStacking = r.AllowCouponStacking;
        settings.AllowCouponWithCashback = r.AllowCouponWithCashback;
        settings.NewUserDays = r.NewUserDays;
        settings.FirstTripCashback = r.FirstTripCashback;
        settings.ReferralEnabled = r.ReferralEnabled;
        settings.ReferrerReward = r.ReferrerReward;
        settings.ReferredReward = r.ReferredReward;
        settings.ReferralApplyDays = r.ReferralApplyDays;
        settings.MaxReferralRewardsPerMonth = r.MaxReferralRewardsPerMonth;
        settings.UpdatedAt = DateTime.UtcNow;
        settings.UpdatedById = adminId;
        Audit(adminId, "settings:update", "settings", null, new { before, after = r });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return new(StatusCodes.Status409Conflict, "Another admin changed the settings at the same moment. Reload and try again.");
        }
        config.Forget();
        return new(StatusCodes.Status200OK, "Reward settings saved. They apply to trips completed from now on.", settings);
    }

    // Changes the thresholds and benefits of the existing tiers (the tiers themselves stay). Each tier must need
    // more trips than the one below it, and the lowest tier needs none.
    public async Task<PromoResult<List<MembershipTier>>> SaveTiersAsync(Guid adminId, List<TierRequest> requests)
    {
        var tiers = await db.MembershipTiers.OrderBy(t => t.Rank).ToListAsync();
        if (requests.Count != tiers.Count || requests.Select(r => r.Code).Distinct().Count() != requests.Count
            || requests.Any(r => tiers.All(t => t.Code != r.Code)))
        {
            return new(StatusCodes.Status400BadRequest, $"Send every tier once: {string.Join(", ", tiers.Select(t => t.Code))}.");
        }
        var ordered = tiers.Select(t => requests.First(r => r.Code == t.Code)).ToList();
        if (ordered[0].MinTrips != 0 || ordered[0].MinSpend != 0)
        {
            return new(StatusCodes.Status400BadRequest, $"{ordered[0].Name} is the starting tier, so it must need 0 trips and 0 spending.");
        }
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].MinTrips <= ordered[i - 1].MinTrips)
            {
                return new(StatusCodes.Status400BadRequest, $"{ordered[i].Name} must need more trips than {ordered[i - 1].Name}.");
            }
        }
        var now = DateTime.UtcNow;
        foreach (var tier in tiers)
        {
            var r = requests.First(x => x.Code == tier.Code);
            tier.Name = r.Name.Trim();
            tier.MinTrips = r.MinTrips;
            tier.MinSpend = r.MinSpend;
            tier.ExtraCashbackPercent = r.ExtraCashbackPercent;
            tier.ExtraCapPerTrip = r.ExtraCapPerTrip;
            tier.ExtraMonthlyCap = r.ExtraMonthlyCap;
            tier.Benefits = r.Benefits?.Trim() ?? "";
            tier.UpdatedAt = now;
        }
        Audit(adminId, "tiers:update", "tiers", null, requests);
        await db.SaveChangesAsync();
        config.Forget();
        return new(StatusCodes.Status200OK, "Membership tiers saved.", tiers);
    }

    // ---- Referrals ----

    public async Task<object> ReferralsAsync(string? status, int take = 100)
    {
        var rows = await db.Referrals.AsNoTracking()
            .Where(r => status == null || r.Status == status)
            .OrderByDescending(r => r.CreatedAt).Take(take)
            .Select(r => new
            {
                id = r.Id, r.Code, r.Status, r.CreatedAt, r.QualifiedAt, r.RewardedAt, r.ReferrerReward, r.ReferredReward, r.Note,
                referrer = new { id = r.ReferrerId, r.Referrer!.Name, r.Referrer.Email },
                referred = new { id = r.ReferredUserId, r.ReferredUser!.Name, r.ReferredUser.Email },
            })
            .ToListAsync();
        return rows;
    }

    public async Task<PromoResult<Referral>> RejectReferralAsync(Guid adminId, Guid id, string? note)
    {
        var referral = await db.Referrals.FirstOrDefaultAsync(r => r.Id == id);
        if (referral is null)
        {
            return new(StatusCodes.Status404NotFound, "Referral not found.");
        }
        if (referral.Status is ReferralStatus.Rewarded or ReferralStatus.Rejected)
        {
            return new(StatusCodes.Status409Conflict, $"This referral is already {referral.Status}.");
        }
        referral.Status = ReferralStatus.Rejected;
        referral.Note = string.IsNullOrWhiteSpace(note) ? "Rejected by an admin." : Clip(note.Trim(), 200);
        Audit(adminId, "referral:reject", "referral", referral.Id, new { referral.Note });
        await db.SaveChangesAsync();
        return new(StatusCodes.Status200OK, "Referral rejected. No rewards will be paid for it.", referral);
    }

    // ---- Statistics ----

    public async Task<object> StatisticsAsync(DateTime now)
    {
        var since = now.AddDays(-14);
        // Every count refers to the settings row (s), so EF Core runs them all as subqueries of one SELECT.
        await config.SettingsAsync();
        var totals = await db.RewardSettings.AsNoTracking().Where(s => s.Id == 1).Select(s => new
        {
            coupons = db.Coupons.Count(c => s.Id == 1 && c.DeletedAt == null),
            activeCoupons = db.Coupons.Count(c => s.Id == 1 && c.DeletedAt == null && c.IsActive && (c.ExpiresAt == null || c.ExpiresAt > now)),
            redemptions = db.CouponRedemptions.Count(r => s.Id == 1),
            discountGiven = db.CouponRedemptions.Where(r => s.Id == 1).Sum(r => (decimal?)r.Discount) ?? 0,
            cashbackCredited = db.CashbackTransactions.Where(c => s.Id == 1 && c.Amount > 0).Sum(c => (decimal?)c.Amount) ?? 0,
            cashbackFromTrips = db.CashbackTransactions.Where(c => s.Id == 1 && c.Source == CashbackSources.Trip).Sum(c => (decimal?)c.Amount) ?? 0,
            cashbackFirstTrip = db.CashbackTransactions.Where(c => s.Id == 1 && c.Source == CashbackSources.FirstTrip).Sum(c => (decimal?)c.Amount) ?? 0,
            cashbackReferral = db.CashbackTransactions.Where(c => s.Id == 1 && c.Source == CashbackSources.Referral).Sum(c => (decimal?)c.Amount) ?? 0,
            cashbackMovedToWallet = -(db.CashbackTransactions.Where(c => s.Id == 1 && c.Source == CashbackSources.Redeemed).Sum(c => (decimal?)c.Amount) ?? 0),
            cashbackExpired = -(db.CashbackTransactions.Where(c => s.Id == 1 && c.Source == CashbackSources.Expired).Sum(c => (decimal?)c.Amount) ?? 0),
            cashbackOutstanding = db.Offers.Where(o => s.Id == 1).Sum(o => (decimal?)o.Cashback) ?? 0,
            eligibleTrips = db.TripRewards.Count(r => s.Id == 1 && r.Eligible),
            ineligibleTrips = db.TripRewards.Count(r => s.Id == 1 && !r.Eligible),
            referralsPending = db.Referrals.Count(r => s.Id == 1 && r.Status == ReferralStatus.Pending),
            referralsRewarded = db.Referrals.Count(r => s.Id == 1 && r.Status == ReferralStatus.Rewarded),
            referralsQualified = db.Referrals.Count(r => s.Id == 1 && r.Status == ReferralStatus.Qualified),
            referralsRejected = db.Referrals.Count(r => s.Id == 1 && r.Status == ReferralStatus.Rejected),
            liveCampaigns = db.RewardCampaigns.Count(c => s.Id == 1 && c.Status == CampaignStatus.Active && c.StartsAt <= now && c.EndsAt > now),
        }).FirstOrDefaultAsync();
        var tiers = await config.TiersAsync();
        var members = await db.UserMemberships.AsNoTracking().GroupBy(m => m.TierCode).Select(g => new { tier = g.Key, count = g.Count() }).ToListAsync();
        var top = await db.CouponRedemptions.AsNoTracking().GroupBy(r => r.Coupon!.Code)
            .Select(g => new { code = g.Key, redemptions = g.Count(), discount = g.Sum(r => r.Discount) })
            .OrderByDescending(x => x.redemptions).Take(5).ToListAsync();
        var recentRedemptions = await db.CouponRedemptions.AsNoTracking().Where(r => r.CreatedAt >= since)
            .Select(r => new { r.CreatedAt, r.Discount }).ToListAsync();
        var recentCashback = await db.CashbackTransactions.AsNoTracking().Where(c => c.CreatedAt >= since && c.Amount > 0)
            .Select(c => new { c.CreatedAt, c.Amount }).ToListAsync();
        var days = Enumerable.Range(0, 14).Select(i =>
        {
            var day = DhakaTime.FromUtc(now).Date.AddDays(i - 13);
            bool On(DateTime utc) => DhakaTime.FromUtc(utc).Date == day;
            return new
            {
                date = day.ToString("yyyy-MM-dd"),
                redemptions = recentRedemptions.Count(r => On(r.CreatedAt)),
                discount = recentRedemptions.Where(r => On(r.CreatedAt)).Sum(r => r.Discount),
                cashback = recentCashback.Where(c => On(c.CreatedAt)).Sum(c => c.Amount),
            };
        }).ToList();
        var campaigns = await db.RewardCampaigns.AsNoTracking().OrderByDescending(c => c.CreatedAt).Take(10)
            .Select(c => new { id = c.Id, c.Name, c.Status, c.Spent, c.Budget, c.Redemptions, c.MaxRedemptions, c.EndsAt }).ToListAsync();
        return new
        {
            totals,
            membership = tiers.Select(t => new { tier = t.Code, name = t.Name, count = members.FirstOrDefault(m => m.tier == t.Code)?.count ?? 0 }),
            topCoupons = top,
            days,
            campaigns,
        };
    }

    public async Task<(List<CouponRedemption> Items, int Total)> RedemptionsAsync(string? code, int page, int pageSize)
    {
        var normalized = PromotionRules.NormalizeCode(code);
        var query = db.CouponRedemptions.Where(r => normalized == null || r.Coupon!.Code == normalized);
        var items = await query.Include(r => r.Coupon).Include(r => r.User).Include(r => r.Trip)
            .OrderByDescending(r => r.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .AsNoTracking().AsSingleQuery().ToListAsync();
        var total = page == 1 && items.Count < pageSize ? items.Count : await query.CountAsync();
        return (items, total);
    }

    // Activity that may be abuse, for an admin to look at. Nothing is blocked automatically.
    public async Task<List<SuspiciousItem>> SuspiciousAsync(DateTime now)
    {
        var day = now.AddDays(-1);
        var settings = await config.SettingsAsync();
        var list = new List<SuspiciousItem>();

        var redeemers = await db.CouponRedemptions.AsNoTracking().Where(r => r.CreatedAt >= day)
            .GroupBy(r => r.UserId).Where(g => g.Count() >= 5)
            .Select(g => new { UserId = g.Key, Count = g.Count(), Total = g.Sum(r => r.Discount) }).ToListAsync();
        var referrers = await db.Referrals.AsNoTracking().Where(r => r.CreatedAt >= day)
            .GroupBy(r => r.ReferrerId).Where(g => g.Count() >= 5)
            .Select(g => new { UserId = g.Key, Count = g.Count() }).ToListAsync();
        var attempts = await db.RewardAuditLogs.AsNoTracking()
            .Where(a => a.CreatedAt >= day && a.ActorId != null && (a.Action == "referral:self" || a.Action == "referral:mutual" || a.Action == "referral:unknown-code"))
            .GroupBy(a => a.ActorId!.Value).Where(g => g.Count() >= 3)
            .Select(g => new { UserId = g.Key, Count = g.Count() }).ToListAsync();
        var monthStart = PromotionRules.MonthStartUtc(now);
        var capped = settings.CashbackMaxPerUserMonthly > 0
            ? await db.CashbackTransactions.AsNoTracking().Where(c => c.Source == CashbackSources.Trip && c.CreatedAt >= monthStart)
                .GroupBy(c => c.UserId).Where(g => g.Sum(c => c.Amount) >= settings.CashbackMaxPerUserMonthly)
                .Select(g => new { UserId = g.Key, Total = g.Sum(c => c.Amount), Count = g.Count() }).ToListAsync()
            : [];

        var ids = redeemers.Select(x => x.UserId).Concat(referrers.Select(x => x.UserId)).Concat(attempts.Select(x => x.UserId)).Concat(capped.Select(x => x.UserId)).Distinct().ToList();
        var people = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.Name, u.Email }).ToDictionaryAsync(u => u.Id);
        SuspiciousItem Item(string kind, Guid id, int count, string detail) =>
            new(kind, id, people.GetValueOrDefault(id)?.Name ?? "Unknown", people.GetValueOrDefault(id)?.Email ?? "", count, detail);
        list.AddRange(redeemers.Select(x => Item("many-redemptions", x.UserId, x.Count, $"{x.Count} coupons redeemed in 24 hours, saving {PromotionRules.Money(x.Total)}.")));
        list.AddRange(referrers.Select(x => Item("many-referrals", x.UserId, x.Count, $"{x.Count} people signed up with their code in 24 hours.")));
        list.AddRange(attempts.Select(x => Item("referral-attempts", x.UserId, x.Count, $"{x.Count} refused referral attempts (own code, each other's code or unknown codes) in 24 hours.")));
        list.AddRange(capped.Select(x => Item("cashback-limit", x.UserId, x.Count, $"Reached the monthly cashback limit: {PromotionRules.Money(x.Total)} from {x.Count} trips this month.")));
        return list;
    }

    public Task<List<RewardAuditLog>> AuditAsync(int take = 100) =>
        db.RewardAuditLogs.AsNoTracking().OrderByDescending(a => a.CreatedAt).Take(take).ToListAsync();

    public void AuditTripProcessing(Guid adminId, Guid tripId, TripRewardOutcome outcome) =>
        Audit(adminId, "trip:process-rewards", "trip", tripId, new { outcome.Processed, outcome.Eligible, outcome.Cashback, outcome.Message });

    public Task SaveAsync() => db.SaveChangesAsync();
}
