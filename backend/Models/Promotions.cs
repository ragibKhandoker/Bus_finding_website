using System.ComponentModel.DataAnnotations;

namespace SmartCommute.Server.Models;

// ---- F15: rewards, cashback, coupons, campaigns, membership and referrals ----
//
// Every amount is in taka and is worked out on the server from the trip the server recorded. The tables below
// keep the configuration (RewardSettings, MembershipTier, Coupon, RewardCampaign) apart from what was issued
// (TripReward, CashbackTransaction, UserCoupon, CouponRedemption, Referral), and unique keys on what was issued
// make every reward happen at most once per event.

public static class PromoDiscountTypes
{
    public const string Fixed = "fixed";
    public const string Percent = "percent";

    public static readonly string[] All = [Fixed, Percent];
}

public static class CouponTypes
{
    public const string Welcome = "welcome";
    public const string FirstTrip = "first-trip";
    public const string Weekend = "weekend";
    public const string Seasonal = "seasonal";
    public const string Referral = "referral";
    public const string Fixed = "fixed";
    public const string Percentage = "percentage";
    public const string Campaign = "campaign";

    public static readonly string[] All = [Welcome, FirstTrip, Weekend, Seasonal, Referral, Fixed, Percentage, Campaign];

    // Types that are handed to particular users (and never shown to everyone).
    public static readonly string[] Issued = [Welcome, FirstTrip, Referral];
}

// Who may use a coupon.
public static class CouponAudiences
{
    public const string Everyone = "all";            // any signed-in user
    public const string NewUsers = "new-users";      // accounts younger than RewardSettings.NewUserDays with no eligible trip before
    public const string Assigned = "assigned";       // only users it was issued to (welcome, first-trip, referral, admin)

    public static readonly string[] All = [Everyone, NewUsers, Assigned];
}

public static class CampaignTypes
{
    public const string Cashback = "cashback";
    public const string Weekend = "weekend";
    public const string FirstTrip = "first-trip";
    public const string Festival = "festival";        // Eid, Pohela Boishakh and other seasonal offers
    public const string Welcome = "welcome";
    public const string TierExclusive = "tier-exclusive";

    public static readonly string[] All = [Cashback, Weekend, FirstTrip, Festival, Welcome, TierExclusive];
}

public static class CampaignStatus
{
    public const string Active = "active";
    public const string Paused = "paused";
    public const string Expired = "expired";

    public static readonly string[] All = [Active, Paused, Expired];
}

public static class CashbackSources
{
    public const string Trip = "trip";               // earned on an eligible trip (base rate, tier bonus and campaign)
    public const string FirstTrip = "first-trip";    // the first eligible trip bonus
    public const string Referral = "referral";       // a referral reward, for either side
    public const string Redeemed = "redeemed";       // moved into the wallet balance
    public const string Expired = "expired";         // not used before it expired

    public static readonly string[] All = [Trip, FirstTrip, Referral, Redeemed, Expired];
}

public static class ReferralStatus
{
    public const string Pending = "pending";         // signed up with the code, no eligible trip yet
    public const string Qualified = "qualified";     // made the qualifying trip, but the rewards were not paid (programme off or the monthly cap reached)
    public const string Rewarded = "rewarded";       // both sides were paid
    public const string Rejected = "rejected";       // an admin rejected it

    public static readonly string[] All = [Pending, Qualified, Rewarded, Rejected];
}

public static class RedemptionModes
{
    public const string Wallet = "wallet";           // the saving was paid into the wallet (the trip was already over)
    public const string Gpay = "gpay";               // taken off the amount paid with GPay

    public static readonly string[] All = [Wallet, Gpay];
}

// The admin-editable reward rules. There is exactly one row (Id 1), created with the defaults from
// appsettings.json (section "Promotions") the first time the server needs it.
public class RewardSettings
{
    public int Id { get; set; } = 1;

    public bool CashbackEnabled { get; set; } = true;
    public decimal CashbackPercent { get; set; }
    public decimal CashbackFixed { get; set; }
    public decimal CashbackMinFare { get; set; }
    public decimal CashbackMaxPerTrip { get; set; }
    public decimal CashbackMaxPerUserMonthly { get; set; }
    // Days before earned cashback that is still in the cashback balance expires; 0 means it never does.
    public int CashbackExpiryDays { get; set; }

    // Whether a second coupon may be used on the same trip, and whether a coupon may be used on a trip that
    // earned cashback.
    public bool AllowCouponStacking { get; set; }
    public bool AllowCouponWithCashback { get; set; } = true;

    // How long an account counts as new, for new-user coupons and campaigns.
    public int NewUserDays { get; set; }

    public decimal FirstTripCashback { get; set; }

    public bool ReferralEnabled { get; set; } = true;
    public decimal ReferrerReward { get; set; }
    public decimal ReferredReward { get; set; }
    // How many days after signing up a user may still enter a friend's code.
    public int ReferralApplyDays { get; set; }
    // The most referral rewards one person can earn in a month.
    public int MaxReferralRewardsPerMonth { get; set; }

    // When the reward engine started; trips that ended before this never earn rewards retroactively.
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedById { get; set; }

    public uint Version { get; set; }
}

// A loyalty tier. A user is on the highest tier whose trip count (or, when set, eligible spending) they reach.
public class MembershipTier
{
    [MaxLength(20)]
    public string Code { get; set; } = "";

    [MaxLength(40)]
    public string Name { get; set; } = "";

    // Order from lowest (0) to highest.
    public int Rank { get; set; }

    public int MinTrips { get; set; }
    // 0 means spending does not count towards this tier.
    public decimal MinSpend { get; set; }

    // Added to the cashback percentage, and to the per-trip and monthly cashback caps.
    public decimal ExtraCashbackPercent { get; set; }
    public decimal ExtraCapPerTrip { get; set; }
    public decimal ExtraMonthlyCap { get; set; }

    [MaxLength(300)]
    public string Benefits { get; set; } = "";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// Where a user stands: their tier and the eligible trips and spending it is based on.
public class UserMembership
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(20)]
    public string TierCode { get; set; } = "";

    public int EligibleTrips { get; set; }
    public decimal EligibleSpend { get; set; }

    public DateTime? UpgradedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class RewardCampaign
{
    public Guid Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = "";

    [MaxLength(500)]
    public string Description { get; set; } = "";

    [MaxLength(30)]
    public string Type { get; set; } = CampaignTypes.Cashback;

    // A banner picture (https URL) and the colour theme of the artwork drawn when there is none.
    [MaxLength(500)]
    public string? BannerUrl { get; set; }

    [MaxLength(20)]
    public string Theme { get; set; } = "green";

    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = CampaignStatus.Active;

    // The extra cashback the campaign pays on an eligible trip.
    public decimal CashbackPercent { get; set; }
    public decimal CashbackFixed { get; set; }
    public decimal? MaxCashbackPerTrip { get; set; }

    // Who and which trips qualify.
    public decimal MinFare { get; set; }
    [MaxLength(20)]
    public string? MinTier { get; set; }
    public bool NewUsersOnly { get; set; }
    public bool WeekendOnly { get; set; }
    public bool FirstTripOnly { get; set; }

    // Limits on what it may give away in total: taka (cashback and coupon savings) and number of rewards.
    public decimal? Budget { get; set; }
    public decimal Spent { get; set; }
    public int? MaxRedemptions { get; set; }
    public int Redemptions { get; set; }

    // When users were told about it, so they are told once.
    public DateTime? AnnouncedAt { get; set; }

    public Guid? CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class Coupon
{
    public Guid Id { get; set; }

    // Always stored upper-case; unique.
    [MaxLength(30)]
    public string Code { get; set; } = "";

    [MaxLength(100)]
    public string Title { get; set; } = "";

    [MaxLength(500)]
    public string Description { get; set; } = "";

    [MaxLength(20)]
    public string Type { get; set; } = CouponTypes.Percentage;

    [MaxLength(10)]
    public string DiscountType { get; set; } = PromoDiscountTypes.Percent;

    public decimal DiscountValue { get; set; }
    public decimal MinFare { get; set; }
    public decimal? MaxDiscount { get; set; }

    public DateTime StartsAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }

    // Null means no overall limit.
    public int? TotalLimit { get; set; }
    public int PerUserLimit { get; set; } = 1;
    public int RedemptionCount { get; set; }

    public Guid? CampaignId { get; set; }
    public RewardCampaign? Campaign { get; set; }

    public bool IsActive { get; set; } = true;

    [MaxLength(20)]
    public string Audience { get; set; } = CouponAudiences.Everyone;

    // Only members of this tier or higher may use it.
    [MaxLength(20)]
    public string? MinTier { get; set; }

    // Only on trips on this route (route number, e.g. "8"), when set.
    [MaxLength(40)]
    public string? RouteNumber { get; set; }

    // Only on trips taken on Friday or Saturday (the weekend in Bangladesh), Dhaka time.
    public bool WeekendOnly { get; set; }

    // For coupons handed to users: how many days the user's copy stays valid after it is issued.
    public int? ValidDaysAfterIssue { get; set; }

    public Guid? CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    // Deleted coupons are kept (their redemptions refer to them) but can never be used or shown again.
    public DateTime? DeletedAt { get; set; }
}

// A coupon handed to one user (welcome, first trip, referral, or by an admin). Reference is unique per user, so the
// same reward can never be issued twice.
public class UserCoupon
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid CouponId { get; set; }
    public Coupon? Coupon { get; set; }

    [MaxLength(20)]
    public string Source { get; set; } = "";

    [MaxLength(120)]
    public string Reference { get; set; } = "";

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime? ExpiryNotifiedAt { get; set; }
}

// One coupon used on one trip. Unique per (trip, coupon).
public class CouponRedemption
{
    public Guid Id { get; set; }
    public Guid CouponId { get; set; }
    public Coupon? Coupon { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid TripId { get; set; }
    public Trip? Trip { get; set; }
    public Guid? UserCouponId { get; set; }
    public Guid? CampaignId { get; set; }

    // The trip's fare, the saving and what was left to pay.
    public decimal Fare { get; set; }
    public decimal Discount { get; set; }
    public decimal FinalFare { get; set; }

    [MaxLength(10)]
    public string Mode { get; set; } = RedemptionModes.Wallet;

    public Guid? WalletTransactionId { get; set; }
    public Guid? GpayTransactionId { get; set; }

    [MaxLength(100)]
    public string? Reference { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// The reward engine's record of a completed trip: whether it was eligible and what cashback it earned. TripId is
// the key, so the same trip can only ever be processed once, however often the completion event is retried.
public class TripReward
{
    public Guid TripId { get; set; }
    public Trip? Trip { get; set; }
    public Guid UserId { get; set; }

    public decimal Fare { get; set; }
    public bool Eligible { get; set; }

    [MaxLength(200)]
    public string? Reason { get; set; }

    public decimal Cashback { get; set; }

    [MaxLength(20)]
    public string? TierCode { get; set; }

    public Guid? CampaignId { get; set; }

    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

// The cashback ledger: + earned (each one a "lot" with what is left of it and when it expires), - moved into the
// wallet or expired. EventKey is unique, so one event (a trip, a first trip, a referral) pays once.
public class CashbackTransaction
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public decimal Amount { get; set; }

    [MaxLength(20)]
    public string Source { get; set; } = CashbackSources.Trip;

    [MaxLength(200)]
    public string Description { get; set; } = "";

    public Guid? TripId { get; set; }
    public Guid? CampaignId { get; set; }
    public Guid? ReferralId { get; set; }

    [MaxLength(120)]
    public string? EventKey { get; set; }

    // For earned cashback: the part not yet moved to the wallet or expired, and when it expires.
    public decimal Remaining { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? ExpiredAt { get; set; }

    // How a trip's cashback was made up.
    public decimal? FareBasis { get; set; }
    public decimal? BaseAmount { get; set; }
    public decimal? TierBonus { get; set; }
    public decimal? CampaignAmount { get; set; }

    public Guid? OfferTransactionId { get; set; }
    public Guid? WalletTransactionId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Referral
{
    public Guid Id { get; set; }
    public Guid ReferrerId { get; set; }
    public User? Referrer { get; set; }
    // Unique: a person can be referred once.
    public Guid ReferredUserId { get; set; }
    public User? ReferredUser { get; set; }

    [MaxLength(12)]
    public string Code { get; set; } = "";

    [MaxLength(20)]
    public string Status { get; set; } = ReferralStatus.Pending;

    public Guid? QualifyingTripId { get; set; }
    public decimal ReferrerReward { get; set; }
    public decimal ReferredReward { get; set; }

    [MaxLength(200)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? QualifiedAt { get; set; }
    public DateTime? RewardedAt { get; set; }
}

// Who changed reward configuration, and unusual reward activity worth a look.
public class RewardAuditLog
{
    public Guid Id { get; set; }
    public Guid? ActorId { get; set; }

    [MaxLength(60)]
    public string Action { get; set; } = "";

    [MaxLength(40)]
    public string EntityType { get; set; } = "";

    [MaxLength(60)]
    public string? EntityId { get; set; }

    [MaxLength(1000)]
    public string Details { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
