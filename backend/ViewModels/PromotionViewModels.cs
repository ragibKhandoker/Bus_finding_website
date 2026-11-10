using System.ComponentModel.DataAnnotations;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Server.ViewModels;

// ---- F15: what the Offers page and the admin's Offers section read ----

public class CouponViewModel
{
    public string Id { get; init; } = "";
    public string Code { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Type { get; init; } = "";
    public string DiscountType { get; init; } = "";
    public decimal DiscountValue { get; init; }
    public decimal MinFare { get; init; }
    public decimal? MaxDiscount { get; init; }
    public string Saving { get; init; } = "";
    public DateTime StartsAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public int? TotalLimit { get; init; }
    public int PerUserLimit { get; init; }
    public int RedemptionCount { get; init; }
    public string? CampaignId { get; init; }
    public string? CampaignName { get; init; }
    public bool IsActive { get; init; }
    public string Audience { get; init; } = "";
    public string? MinTier { get; init; }
    public string? RouteNumber { get; init; }
    public bool WeekendOnly { get; init; }
    public int? ValidDaysAfterIssue { get; init; }
    public List<string> Conditions { get; init; } = [];
    public DateTime CreatedAt { get; init; }

    public static CouponViewModel From(Coupon c, IReadOnlyList<MembershipTier> tiers) => new()
    {
        Id = c.Id.ToString(), Code = c.Code, Title = c.Title, Description = c.Description, Type = c.Type, DiscountType = c.DiscountType,
        DiscountValue = c.DiscountValue, MinFare = c.MinFare, MaxDiscount = c.MaxDiscount, Saving = PromotionRules.SavingText(c),
        StartsAt = c.StartsAt, ExpiresAt = c.ExpiresAt, TotalLimit = c.TotalLimit, PerUserLimit = c.PerUserLimit, RedemptionCount = c.RedemptionCount,
        CampaignId = c.CampaignId?.ToString(), CampaignName = c.Campaign?.Name, IsActive = c.IsActive, Audience = c.Audience, MinTier = c.MinTier,
        RouteNumber = c.RouteNumber, WeekendOnly = c.WeekendOnly, ValidDaysAfterIssue = c.ValidDaysAfterIssue,
        Conditions = PromotionRules.Conditions(c, tiers), CreatedAt = c.CreatedAt,
    };
}

// One coupon on the user's list.
public class MyCouponViewModel
{
    public CouponViewModel Coupon { get; init; } = new();
    public string Status { get; init; } = "";
    public string? Note { get; init; }
    public int UsesLeft { get; init; }
    public int? TotalLeft { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public string? Source { get; init; }
    public DateTime? IssuedAt { get; init; }
    public RedemptionViewModel? Redemption { get; init; }

    public static MyCouponViewModel From(CouponListItem i, IReadOnlyList<MembershipTier> tiers) => new()
    {
        Coupon = CouponViewModel.From(i.Coupon, tiers), Status = i.Status, Note = i.Note, UsesLeft = i.UsesLeft, TotalLeft = i.TotalLeft,
        ExpiresAt = i.ExpiresAt, Source = i.Copy?.Source, IssuedAt = i.Copy?.IssuedAt,
        Redemption = i.Redemption is null ? null : RedemptionViewModel.From(i.Redemption, i.Note),
    };
}

public class RedemptionViewModel
{
    public string Id { get; init; } = "";
    public string Code { get; init; } = "";
    public string TripId { get; init; } = "";
    public string? Trip { get; init; }
    public decimal Fare { get; init; }
    public decimal Discount { get; init; }
    public decimal FinalFare { get; init; }
    public string Mode { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public string? UserName { get; init; }
    public string? UserEmail { get; init; }

    public static RedemptionViewModel From(CouponRedemption r, string? trip = null, bool withUser = false) => new()
    {
        Id = r.Id.ToString(), Code = r.Coupon?.Code ?? "", TripId = r.TripId.ToString(),
        Trip = trip ?? (r.Trip is null ? null : $"{r.Trip.BusName}: {r.Trip.Source} to {r.Trip.Destination}"),
        Fare = r.Fare, Discount = r.Discount, FinalFare = r.FinalFare, Mode = r.Mode, CreatedAt = r.CreatedAt,
        UserName = withUser ? r.User?.Name : null, UserEmail = withUser ? r.User?.Email : null,
    };
}

public class CouponQuoteViewModel
{
    public bool Valid { get; init; }
    public string? Message { get; init; }
    public CouponViewModel? Coupon { get; init; }
    public string? TripId { get; init; }
    public string? Trip { get; init; }
    public decimal OriginalFare { get; init; }
    public decimal AlreadySaved { get; init; }
    public decimal Discount { get; init; }
    public decimal FinalFare { get; init; }
    public decimal CashbackEarned { get; init; }

    public static CouponQuoteViewModel From(CouponQuote q, IReadOnlyList<MembershipTier> tiers) => new()
    {
        Valid = q.Ok, Message = q.Ok ? $"{q.Coupon!.Code} saves you {PromotionRules.Money(q.Discount)} on this trip." : q.Error,
        Coupon = q.Coupon is null ? null : CouponViewModel.From(q.Coupon, tiers), TripId = q.TripId?.ToString(), Trip = q.TripLabel,
        OriginalFare = q.Fare, AlreadySaved = q.CoveredBefore, Discount = q.Discount, FinalFare = q.Ok ? q.FinalFare : Math.Max(0, q.Fare - q.CoveredBefore),
        CashbackEarned = q.CashbackEarned,
    };
}

public class EligibleTripViewModel
{
    public string TripId { get; init; } = "";
    public string BusName { get; init; } = "";
    public string? RouteNumber { get; init; }
    public string Source { get; init; } = "";
    public string Destination { get; init; } = "";
    public DateTime EndedAt { get; init; }
    public decimal Fare { get; init; }
    public decimal CouponSavings { get; init; }
    public decimal CreditSavings { get; init; }
    public decimal LeftToPay { get; init; }
    public decimal Cashback { get; init; }
    public bool CashbackProcessed { get; init; }
    public bool Paid { get; init; }
    public bool CanUseCoupon { get; init; }
    public List<string> Coupons { get; init; } = [];

    public static EligibleTripViewModel From(EligibleTripView t) => new()
    {
        TripId = t.TripId.ToString(), BusName = t.BusName, RouteNumber = t.RouteNumber, Source = t.Source, Destination = t.Destination, EndedAt = t.EndedAt,
        Fare = t.Fare, CouponSavings = t.CouponSavings, CreditSavings = t.CreditSavings, LeftToPay = t.LeftToPay, Cashback = t.Cashback,
        CashbackProcessed = t.CashbackProcessed, Paid = t.Paid, CanUseCoupon = t.CanUseCoupon, Coupons = t.Coupons,
    };
}

public class CashbackEntryViewModel
{
    public string Id { get; init; } = "";
    public decimal Amount { get; init; }
    public string Source { get; init; } = "";
    public string Description { get; init; } = "";
    public string? TripId { get; init; }
    public decimal Remaining { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public DateTime? ExpiredAt { get; init; }
    public decimal? FareBasis { get; init; }
    public decimal? BaseAmount { get; init; }
    public decimal? TierBonus { get; init; }
    public decimal? CampaignAmount { get; init; }
    public DateTime CreatedAt { get; init; }

    public static CashbackEntryViewModel From(CashbackTransaction c) => new()
    {
        Id = c.Id.ToString(), Amount = c.Amount, Source = c.Source, Description = c.Description, TripId = c.TripId?.ToString(), Remaining = c.Remaining,
        ExpiresAt = c.ExpiresAt, ExpiredAt = c.ExpiredAt, FareBasis = c.FareBasis, BaseAmount = c.BaseAmount, TierBonus = c.TierBonus,
        CampaignAmount = c.CampaignAmount, CreatedAt = c.CreatedAt,
    };
}

public class TierViewModel
{
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public int Rank { get; init; }
    public int MinTrips { get; init; }
    public decimal MinSpend { get; init; }
    public decimal ExtraCashbackPercent { get; init; }
    public decimal ExtraCapPerTrip { get; init; }
    public decimal ExtraMonthlyCap { get; init; }
    public string Benefits { get; init; } = "";

    public static TierViewModel From(MembershipTier t) => new()
    {
        Code = t.Code, Name = t.Name, Rank = t.Rank, MinTrips = t.MinTrips, MinSpend = t.MinSpend, ExtraCashbackPercent = t.ExtraCashbackPercent,
        ExtraCapPerTrip = t.ExtraCapPerTrip, ExtraMonthlyCap = t.ExtraMonthlyCap, Benefits = t.Benefits,
    };
}

public class MembershipViewModel
{
    public TierViewModel Tier { get; init; } = new();
    public TierViewModel? Next { get; init; }
    public int EligibleTrips { get; init; }
    public decimal EligibleSpend { get; init; }
    public int TripsToNext { get; init; }
    public decimal SpendToNext { get; init; }
    public int Percent { get; init; }
    public DateTime? UpgradedAt { get; init; }
    public decimal CashbackPercent { get; init; }
    public List<TierViewModel> Tiers { get; init; } = [];

    public static MembershipViewModel From(MembershipView m) => new()
    {
        Tier = TierViewModel.From(m.Tier), Next = m.Next is null ? null : TierViewModel.From(m.Next), EligibleTrips = m.EligibleTrips,
        EligibleSpend = m.EligibleSpend, TripsToNext = m.TripsToNext, SpendToNext = m.SpendToNext, Percent = m.Percent, UpgradedAt = m.UpgradedAt,
        CashbackPercent = m.CashbackPercent, Tiers = m.Tiers.Select(TierViewModel.From).ToList(),
    };
}

public class CampaignViewModel
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Type { get; init; } = "";
    public string? BannerUrl { get; init; }
    public string Theme { get; init; } = "";
    public DateTime StartsAt { get; init; }
    public DateTime EndsAt { get; init; }
    // Stored status, plus "scheduled" for an active campaign that has not started and "expired" once it has ended.
    public string Status { get; init; } = "";
    public string Reward { get; init; } = "";
    public decimal CashbackPercent { get; init; }
    public decimal CashbackFixed { get; init; }
    public decimal? MaxCashbackPerTrip { get; init; }
    public decimal MinFare { get; init; }
    public string? MinTier { get; init; }
    public bool NewUsersOnly { get; init; }
    public bool WeekendOnly { get; init; }
    public bool FirstTripOnly { get; init; }
    public decimal? Budget { get; init; }
    public decimal Spent { get; init; }
    public int? MaxRedemptions { get; init; }
    public int Redemptions { get; init; }
    public decimal? BudgetLeftPercent { get; init; }
    public List<string> Conditions { get; init; } = [];
    public bool? Eligible { get; init; }
    public string? Note { get; init; }

    public static string ShownStatus(RewardCampaign c, DateTime now) =>
        c.Status == CampaignStatus.Active && now >= c.EndsAt ? CampaignStatus.Expired
        : c.Status == CampaignStatus.Active && now < c.StartsAt ? "scheduled"
        : c.Status;

    public static CampaignViewModel From(RewardCampaign c, IReadOnlyList<MembershipTier> tiers, DateTime now, CampaignView? view = null) => new()
    {
        Id = c.Id.ToString(), Name = c.Name, Description = c.Description, Type = c.Type, BannerUrl = c.BannerUrl, Theme = c.Theme,
        StartsAt = c.StartsAt, EndsAt = c.EndsAt, Status = ShownStatus(c, now), Reward = CampaignService.RewardText(c),
        CashbackPercent = c.CashbackPercent, CashbackFixed = c.CashbackFixed, MaxCashbackPerTrip = c.MaxCashbackPerTrip, MinFare = c.MinFare,
        MinTier = c.MinTier, NewUsersOnly = c.NewUsersOnly, WeekendOnly = c.WeekendOnly, FirstTripOnly = c.FirstTripOnly, Budget = c.Budget,
        Spent = c.Spent, MaxRedemptions = c.MaxRedemptions, Redemptions = c.Redemptions,
        BudgetLeftPercent = view?.BudgetLeftPercent ?? (c.Budget is { } b && b > 0 ? Math.Round(Math.Max(0, b - c.Spent) / b * 100, 0) : null),
        Conditions = view?.Conditions ?? CampaignService.Conditions(c, tiers), Eligible = view?.Eligible, Note = view?.Note,
    };
}

public class RewardSettingsViewModel
{
    public bool CashbackEnabled { get; init; }
    public decimal CashbackPercent { get; init; }
    public decimal CashbackFixed { get; init; }
    public decimal CashbackMinFare { get; init; }
    public decimal CashbackMaxPerTrip { get; init; }
    public decimal CashbackMaxPerUserMonthly { get; init; }
    public int CashbackExpiryDays { get; init; }
    public bool AllowCouponStacking { get; init; }
    public bool AllowCouponWithCashback { get; init; }
    public int NewUserDays { get; init; }
    public decimal FirstTripCashback { get; init; }
    public bool ReferralEnabled { get; init; }
    public decimal ReferrerReward { get; init; }
    public decimal ReferredReward { get; init; }
    public int ReferralApplyDays { get; init; }
    public int MaxReferralRewardsPerMonth { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    public static RewardSettingsViewModel From(RewardSettings s) => new()
    {
        CashbackEnabled = s.CashbackEnabled, CashbackPercent = s.CashbackPercent, CashbackFixed = s.CashbackFixed, CashbackMinFare = s.CashbackMinFare,
        CashbackMaxPerTrip = s.CashbackMaxPerTrip, CashbackMaxPerUserMonthly = s.CashbackMaxPerUserMonthly, CashbackExpiryDays = s.CashbackExpiryDays,
        AllowCouponStacking = s.AllowCouponStacking, AllowCouponWithCashback = s.AllowCouponWithCashback, NewUserDays = s.NewUserDays,
        FirstTripCashback = s.FirstTripCashback, ReferralEnabled = s.ReferralEnabled, ReferrerReward = s.ReferrerReward, ReferredReward = s.ReferredReward,
        ReferralApplyDays = s.ReferralApplyDays, MaxReferralRewardsPerMonth = s.MaxReferralRewardsPerMonth, StartedAt = s.StartedAt, UpdatedAt = s.UpdatedAt,
    };
}

// ---- Requests ----

public class CouponCodeRequest
{
    [Required(ErrorMessage = "Enter a coupon code."), StringLength(40)]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Choose the completed trip to use the coupon on.")]
    public Guid? TripId { get; set; }

    // A random id per button press; a repeated request returns the first result.
    [StringLength(100)]
    public string? Reference { get; set; }
}

public class ProcessTripRewardsRequest
{
    [Required(ErrorMessage = "Choose the trip.")]
    public Guid? TripId { get; set; }
}

public class ApplyReferralRequest
{
    [Required(ErrorMessage = "Enter a referral code."), StringLength(20)]
    public string Code { get; set; } = "";
}

// Admin: a coupon. Dates are ISO times, or Dhaka times like "2026-12-01T00:00".
public class CouponRequest
{
    [Required(ErrorMessage = "Enter a coupon code."), StringLength(30)]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Enter a title."), StringLength(100, MinimumLength = 3, ErrorMessage = "The title must be 3 to 100 characters.")]
    public string Title { get; set; } = "";

    [StringLength(500)]
    public string? Description { get; set; }

    [Required, StringLength(20)]
    public string Type { get; set; } = CouponTypes.Percentage;

    [Required, StringLength(10)]
    public string DiscountType { get; set; } = PromoDiscountTypes.Percent;

    [Range(0.01, 100_000, ErrorMessage = "The discount must be more than zero.")]
    public decimal DiscountValue { get; set; }

    [Range(0, 100_000)]
    public decimal MinFare { get; set; }

    [Range(0.01, 100_000, ErrorMessage = "The maximum discount must be more than zero, or left empty.")]
    public decimal? MaxDiscount { get; set; }

    [StringLength(40)]
    public string? StartsAt { get; set; }

    [StringLength(40)]
    public string? ExpiresAt { get; set; }

    [Range(1, 10_000_000, ErrorMessage = "The total limit must be at least 1, or left empty.")]
    public int? TotalLimit { get; set; }

    [Range(1, 1000, ErrorMessage = "The per-user limit must be between 1 and 1000.")]
    public int PerUserLimit { get; set; } = 1;

    public Guid? CampaignId { get; set; }
    public bool IsActive { get; set; } = true;

    [StringLength(20)]
    public string Audience { get; set; } = CouponAudiences.Everyone;

    [StringLength(20)]
    public string? MinTier { get; set; }

    [StringLength(40)]
    public string? RouteNumber { get; set; }

    public bool WeekendOnly { get; set; }

    [Range(1, 3650, ErrorMessage = "Days valid after issue must be between 1 and 3650, or left empty.")]
    public int? ValidDaysAfterIssue { get; set; }
}

public class CampaignRequest
{
    [Required(ErrorMessage = "Enter a campaign name."), StringLength(100, MinimumLength = 3, ErrorMessage = "The name must be 3 to 100 characters.")]
    public string Name { get; set; } = "";

    [StringLength(500)]
    public string? Description { get; set; }

    [Required, StringLength(30)]
    public string Type { get; set; } = CampaignTypes.Cashback;

    [StringLength(500)]
    public string? BannerUrl { get; set; }

    [StringLength(20)]
    public string? Theme { get; set; }

    [Required(ErrorMessage = "Choose when the campaign starts."), StringLength(40)]
    public string StartsAt { get; set; } = "";

    [Required(ErrorMessage = "Choose when the campaign ends."), StringLength(40)]
    public string EndsAt { get; set; } = "";

    [Range(0, 100)]
    public decimal CashbackPercent { get; set; }

    [Range(0, 10_000)]
    public decimal CashbackFixed { get; set; }

    [Range(0, 10_000)]
    public decimal? MaxCashbackPerTrip { get; set; }

    [Range(0, 100_000)]
    public decimal MinFare { get; set; }

    [StringLength(20)]
    public string? MinTier { get; set; }

    public bool NewUsersOnly { get; set; }
    public bool WeekendOnly { get; set; }
    public bool FirstTripOnly { get; set; }

    [Range(0, 100_000_000)]
    public decimal? Budget { get; set; }

    [Range(1, 10_000_000)]
    public int? MaxRedemptions { get; set; }

    [StringLength(20)]
    public string? Status { get; set; }
}

public class CampaignStatusRequest
{
    [Required, StringLength(20)]
    public string Status { get; set; } = "";
}

public class RewardSettingsRequest
{
    public bool CashbackEnabled { get; set; } = true;
    [Range(0, 100)] public decimal CashbackPercent { get; set; }
    [Range(0, 10_000)] public decimal CashbackFixed { get; set; }
    [Range(0, 100_000)] public decimal CashbackMinFare { get; set; }
    [Range(0, 100_000)] public decimal CashbackMaxPerTrip { get; set; }
    [Range(0, 1_000_000)] public decimal CashbackMaxPerUserMonthly { get; set; }
    [Range(0, 3650)] public int CashbackExpiryDays { get; set; }
    public bool AllowCouponStacking { get; set; }
    public bool AllowCouponWithCashback { get; set; } = true;
    [Range(0, 3650)] public int NewUserDays { get; set; }
    [Range(0, 10_000)] public decimal FirstTripCashback { get; set; }
    public bool ReferralEnabled { get; set; } = true;
    [Range(0, 10_000)] public decimal ReferrerReward { get; set; }
    [Range(0, 10_000)] public decimal ReferredReward { get; set; }
    [Range(0, 365)] public int ReferralApplyDays { get; set; }
    [Range(0, 10_000)] public int MaxReferralRewardsPerMonth { get; set; }
}

public class TierRequest
{
    [Required, StringLength(20)] public string Code { get; set; } = "";
    [Required, StringLength(40, MinimumLength = 2)] public string Name { get; set; } = "";
    [Range(0, 100_000)] public int MinTrips { get; set; }
    [Range(0, 10_000_000)] public decimal MinSpend { get; set; }
    [Range(0, 100)] public decimal ExtraCashbackPercent { get; set; }
    [Range(0, 10_000)] public decimal ExtraCapPerTrip { get; set; }
    [Range(0, 1_000_000)] public decimal ExtraMonthlyCap { get; set; }
    [StringLength(300)] public string? Benefits { get; set; }
}

public class TiersRequest
{
    [Required, MinLength(1)]
    public List<TierRequest> Tiers { get; set; } = [];
}

public class IssueCouponRequest
{
    [Required(ErrorMessage = "Enter the user's email address."), EmailAddress, StringLength(255)]
    public string Email { get; set; } = "";
}

public class RejectReferralRequest
{
    [StringLength(200)]
    public string? Note { get; set; }
}
