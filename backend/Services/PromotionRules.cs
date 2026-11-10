using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

// How a trip's cashback is made up. Total is what is credited.
public record CashbackQuote(decimal BaseAmount, decimal TierBonus, decimal CampaignAmount, decimal Total, string? LimitedBy)
{
    public static readonly CashbackQuote None = new(0, 0, 0, 0, null);
}

// What is known about one coupon being used on one trip, gathered by CouponService.
public record CouponContext(
    DateTime NowUtc,
    decimal Fare,
    decimal AlreadyCovered,
    DateTime TripEndedAtUtc,
    string? TripRouteNumber,
    int UserRedemptions,
    UserCoupon? AssignedCopy,
    int TierRank,
    bool IsNewUser,
    bool TripHasOtherCoupon,
    bool TripEarnedCashback,
    RewardSettings Settings,
    IReadOnlyList<MembershipTier> Tiers);

// The answer to "can this coupon be used on this trip?": the saving, or why not.
public record CouponCheck(bool Ok, string? Error, decimal Discount, decimal FinalFare)
{
    public static CouponCheck Fail(string error) => new(false, error, 0, 0);
}

// Where a user stands against the tiers.
public record TierProgress(MembershipTier Current, MembershipTier? Next, int TripsToNext, decimal SpendToNext, int Percent);

// The reward rules, without the database: amounts, eligibility and tiers. Every number comes from the settings
// the admin configured and from the trip as the server recorded it; nothing a client sends is used.
public static partial class PromotionRules
{
    public const decimal MaxMoney = 100_000m;

    // Rewards are rounded down to whole poisha, so a reward is never more than the rule allows.
    public static decimal Floor2(decimal value) => value <= 0 ? 0 : Math.Floor(value * 100) / 100;

    public static string Money(decimal value) => "৳" + value.ToString(value == Math.Floor(value) ? "0" : "0.00", CultureInfo.InvariantCulture);

    // ---- Trips ----

    // Only a trip the server completed itself qualifies: boarded on a real bus, ended at a stop on its route
    // (which is when the server works out the fare), with a fare, after the reward engine started. Trips typed
    // into history by hand carry a fare the client sent, so they never earn rewards.
    public static string? TripProblem(Trip trip, DateTime rewardsStartedAtUtc)
    {
        if (trip.Status != TripStatus.Completed || trip.EndedAt is null)
        {
            return "The trip is not completed yet.";
        }
        if (trip.IndividualBusId is null)
        {
            return "Only trips started by boarding a bus and ended at a stop earn rewards. Trips added to history by hand do not.";
        }
        if (trip.Fare <= 0)
        {
            return "The trip has no fare.";
        }
        if (trip.EndedAt < rewardsStartedAtUtc)
        {
            return "The trip ended before rewards started.";
        }
        return null;
    }

    // Friday and Saturday are the weekend in Bangladesh; the day is the Dhaka day.
    public static bool IsWeekend(DateTime utc) => DhakaTime.FromUtc(utc).DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday;

    // The start of the Dhaka calendar month containing utc, in UTC.
    public static DateTime MonthStartUtc(DateTime utc)
    {
        var local = DhakaTime.FromUtc(utc);
        return DhakaTime.ToUtc(new DateTime(local.Year, local.Month, 1));
    }

    public static bool IsNewUser(DateTime accountCreatedUtc, DateTime nowUtc, RewardSettings settings, int eligibleTripsBefore) =>
        eligibleTripsBefore == 0 && nowUtc - accountCreatedUtc <= TimeSpan.FromDays(settings.NewUserDays);

    // ---- Cashback ----

    // A limit of 0 means "no limit".
    private static decimal Cap(decimal limit) => limit <= 0 ? decimal.MaxValue : limit;

    // The cashback on an eligible trip: the base rate (percentage of the fare plus a fixed amount) and the tier's
    // extra percentage, limited per trip; plus the campaign's own cashback, limited by the campaign. Then the
    // user's monthly limit, and never more than the fare.
    public static CashbackQuote Cashback(decimal fare, RewardSettings settings, MembershipTier? tier, RewardCampaign? campaign,
        decimal earnedThisMonth, decimal? campaignBudgetLeft = null)
    {
        if (fare <= 0)
        {
            return CashbackQuote.None;
        }
        string? limitedBy = null;
        decimal baseAmount = 0, tierBonus = 0;
        if (settings.CashbackEnabled && fare >= settings.CashbackMinFare)
        {
            baseAmount = Floor2(fare * settings.CashbackPercent / 100 + settings.CashbackFixed);
            tierBonus = tier is null ? 0 : Floor2(fare * tier.ExtraCashbackPercent / 100);
            var perTrip = Cap(settings.CashbackMaxPerTrip) == decimal.MaxValue ? decimal.MaxValue : settings.CashbackMaxPerTrip + (tier?.ExtraCapPerTrip ?? 0);
            if (baseAmount + tierBonus > perTrip)
            {
                limitedBy = "per-trip";
                baseAmount = Math.Min(baseAmount, perTrip);
                tierBonus = Math.Max(0, perTrip - baseAmount);
            }
        }

        decimal campaignAmount = 0;
        if (campaign is not null && fare >= campaign.MinFare)
        {
            campaignAmount = Floor2(fare * campaign.CashbackPercent / 100 + campaign.CashbackFixed);
            if (campaign.MaxCashbackPerTrip is { } max && campaignAmount > max)
            {
                campaignAmount = max;
            }
            if (campaignBudgetLeft is { } left && campaignAmount > left)
            {
                campaignAmount = Math.Max(0, Floor2(left));
                limitedBy ??= "campaign-budget";
            }
        }

        var monthly = Cap(settings.CashbackMaxPerUserMonthly) == decimal.MaxValue ? decimal.MaxValue : settings.CashbackMaxPerUserMonthly + (tier?.ExtraMonthlyCap ?? 0);
        var room = Math.Max(0, monthly - earnedThisMonth);
        var total = baseAmount + tierBonus + campaignAmount;
        if (total > room)
        {
            limitedBy = "monthly";
            // Take the campaign part off first, then the tier bonus, then the base.
            var over = total - room;
            var fromCampaign = Math.Min(over, campaignAmount); campaignAmount -= fromCampaign; over -= fromCampaign;
            var fromTier = Math.Min(over, tierBonus); tierBonus -= fromTier; over -= fromTier;
            baseAmount = Math.Max(0, baseAmount - over);
            total = baseAmount + tierBonus + campaignAmount;
        }
        if (total > fare)
        {
            limitedBy = "fare";
            var over = total - fare;
            var fromCampaign = Math.Min(over, campaignAmount); campaignAmount -= fromCampaign; over -= fromCampaign;
            var fromTier = Math.Min(over, tierBonus); tierBonus -= fromTier; over -= fromTier;
            baseAmount = Math.Max(0, baseAmount - over);
            total = baseAmount + tierBonus + campaignAmount;
        }
        return new CashbackQuote(baseAmount, tierBonus, campaignAmount, total, limitedBy);
    }

    // Why a campaign does not apply to this trip, or null when it does.
    public static string? CampaignProblem(RewardCampaign c, DateTime nowUtc, DateTime tripEndedUtc, decimal fare, int tierRank,
        IReadOnlyList<MembershipTier> tiers, bool isNewUser, int eligibleTripsBefore)
    {
        if (c.Status == CampaignStatus.Paused)
        {
            return "The campaign is paused.";
        }
        if (c.Status == CampaignStatus.Expired || nowUtc >= c.EndsAt)
        {
            return "The campaign has ended.";
        }
        if (nowUtc < c.StartsAt)
        {
            return "The campaign has not started yet.";
        }
        if (c.Budget is { } budget && c.Spent >= budget || c.MaxRedemptions is { } max && c.Redemptions >= max)
        {
            return "The campaign's rewards have all been given out.";
        }
        if (fare < c.MinFare)
        {
            return $"The campaign needs a fare of at least {Money(c.MinFare)}.";
        }
        if (c.MinTier is { } minTier && tierRank < RankOf(tiers, minTier))
        {
            return $"The campaign is for {NameOf(tiers, minTier)} members and above.";
        }
        if (c.NewUsersOnly && !isNewUser)
        {
            return "The campaign is for new users.";
        }
        if (c.WeekendOnly && !IsWeekend(tripEndedUtc))
        {
            return "The campaign only counts trips on Friday and Saturday.";
        }
        if (c.FirstTripOnly && eligibleTripsBefore > 0)
        {
            return "The campaign is for a first trip.";
        }
        return null;
    }

    public static bool CampaignIsLive(RewardCampaign c, DateTime nowUtc) =>
        c.Status == CampaignStatus.Active && c.StartsAt <= nowUtc && nowUtc < c.EndsAt
        && !(c.Budget is { } budget && c.Spent >= budget) && !(c.MaxRedemptions is { } max && c.Redemptions >= max);

    // ---- Coupons ----

    public static decimal Discount(Coupon coupon, decimal fare, decimal alreadyCovered)
    {
        var left = Math.Max(0, fare - alreadyCovered);
        var discount = coupon.DiscountType == PromoDiscountTypes.Fixed ? coupon.DiscountValue : Floor2(fare * coupon.DiscountValue / 100);
        if (coupon.MaxDiscount is { } max && discount > max)
        {
            discount = max;
        }
        return Math.Min(Floor2(discount), left);
    }

    // The problems that do not depend on a trip: whether the coupon can be used at all, and by this user.
    public static string? CouponProblem(Coupon coupon, RewardCampaign? campaign, DateTime nowUtc, int userRedemptions, UserCoupon? assigned,
        int tierRank, bool isNewUser, IReadOnlyList<MembershipTier> tiers)
    {
        if (!coupon.IsActive || coupon.DeletedAt is not null)
        {
            return "This coupon is not active.";
        }
        if (nowUtc < coupon.StartsAt)
        {
            return $"This coupon can be used from {DhakaTime.FromUtc(coupon.StartsAt):d MMM yyyy}.";
        }
        if (coupon.ExpiresAt is { } expires && nowUtc >= expires)
        {
            return "This coupon has expired.";
        }
        if (campaign is not null)
        {
            if (campaign.Status == CampaignStatus.Paused)
            {
                return "This coupon's campaign is paused right now.";
            }
            if (campaign.Status == CampaignStatus.Expired || nowUtc >= campaign.EndsAt)
            {
                return "This coupon's campaign has ended.";
            }
            if (nowUtc < campaign.StartsAt)
            {
                return "This coupon's campaign has not started yet.";
            }
            if (campaign.Budget is { } budget && campaign.Spent >= budget || campaign.MaxRedemptions is { } max && campaign.Redemptions >= max)
            {
                return "This campaign's rewards have all been given out.";
            }
        }
        if (coupon.Audience == CouponAudiences.Assigned && assigned is null)
        {
            return "This coupon is not available on your account.";
        }
        if (assigned?.ExpiresAt is { } mine && nowUtc >= mine)
        {
            return "Your copy of this coupon has expired.";
        }
        if (assigned?.UsedAt is not null && coupon.Audience == CouponAudiences.Assigned)
        {
            return "You have already used this coupon.";
        }
        if (coupon.Audience == CouponAudiences.NewUsers && !isNewUser)
        {
            return "This coupon is only for new users.";
        }
        if (coupon.MinTier is { } minTier && tierRank < RankOf(tiers, minTier))
        {
            return $"This coupon is for {NameOf(tiers, minTier)} members and above.";
        }
        if (coupon.TotalLimit is { } total && coupon.RedemptionCount >= total)
        {
            return "This coupon has been fully redeemed.";
        }
        if (userRedemptions >= coupon.PerUserLimit)
        {
            return coupon.PerUserLimit == 1 ? "You have already used this coupon." : $"You have used this coupon the most times allowed ({coupon.PerUserLimit}).";
        }
        return null;
    }

    // Everything: the coupon, the user and the trip. The saving never takes the fare below zero.
    public static CouponCheck Check(Coupon coupon, RewardCampaign? campaign, CouponContext ctx)
    {
        if (CouponProblem(coupon, campaign, ctx.NowUtc, ctx.UserRedemptions, ctx.AssignedCopy, ctx.TierRank, ctx.IsNewUser, ctx.Tiers) is { } problem)
        {
            return CouponCheck.Fail(problem);
        }
        if (coupon.WeekendOnly && !IsWeekend(ctx.TripEndedAtUtc))
        {
            return CouponCheck.Fail("This coupon only works on trips taken on a Friday or Saturday.");
        }
        if (!string.IsNullOrWhiteSpace(coupon.RouteNumber)
            && !string.Equals(coupon.RouteNumber.Trim(), ctx.TripRouteNumber?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return CouponCheck.Fail($"This coupon only works on route {coupon.RouteNumber}.");
        }
        if (ctx.TripHasOtherCoupon && !ctx.Settings.AllowCouponStacking)
        {
            return CouponCheck.Fail("Another coupon was already used on this trip, and coupons cannot be combined.");
        }
        if (ctx.TripEarnedCashback && !ctx.Settings.AllowCouponWithCashback)
        {
            return CouponCheck.Fail("This trip earned cashback, and coupons cannot be combined with cashback.");
        }
        if (ctx.Fare < coupon.MinFare)
        {
            return CouponCheck.Fail($"This coupon needs a fare of at least {Money(coupon.MinFare)}. This trip's fare is {Money(ctx.Fare)}.");
        }
        if (ctx.Fare - ctx.AlreadyCovered <= 0)
        {
            return CouponCheck.Fail("This trip's fare is already fully covered.");
        }
        var discount = Discount(coupon, ctx.Fare, ctx.AlreadyCovered);
        if (campaign?.Budget is { } budget)
        {
            discount = Math.Min(discount, Floor2(budget - campaign.Spent));
        }
        if (discount <= 0)
        {
            return CouponCheck.Fail("This coupon gives no saving on this trip.");
        }
        return new CouponCheck(true, null, discount, ctx.Fare - ctx.AlreadyCovered - discount);
    }

    // Upper-case, no spaces. Null unless it looks like a coupon code at all.
    public static string? NormalizeCode(string? code)
    {
        var text = code?.Trim().ToUpperInvariant().Replace(" ", "") ?? "";
        return CouponCodeRegex().IsMatch(text) ? text : null;
    }

    [GeneratedRegex("^[A-Z0-9_-]{3,30}$")]
    private static partial Regex CouponCodeRegex();

    // A plain-words description of a coupon's saving, e.g. "10% off (up to ৳20)" or "৳15 off".
    public static string SavingText(Coupon c) => c.DiscountType == PromoDiscountTypes.Fixed
        ? $"{Money(c.DiscountValue)} off"
        : $"{c.DiscountValue.ToString("0.##", CultureInfo.InvariantCulture)}% off" + (c.MaxDiscount is { } max ? $" (up to {Money(max)})" : "");

    // The conditions to show on an offer card.
    public static List<string> Conditions(Coupon c, IReadOnlyList<MembershipTier> tiers)
    {
        var list = new List<string>();
        if (c.MinFare > 0) list.Add($"Fare of at least {Money(c.MinFare)}");
        if (c.MaxDiscount is { } max && c.DiscountType == PromoDiscountTypes.Percent) list.Add($"Saves at most {Money(max)}");
        if (c.WeekendOnly) list.Add("Friday and Saturday trips only");
        if (!string.IsNullOrWhiteSpace(c.RouteNumber)) list.Add($"Route {c.RouteNumber} only");
        if (c.MinTier is { } tier) list.Add($"{NameOf(tiers, tier)} members and above");
        if (c.Audience == CouponAudiences.NewUsers) list.Add("New users only");
        if (c.Audience == CouponAudiences.Assigned) list.Add("Issued to your account");
        list.Add(c.PerUserLimit == 1 ? "Once per user" : $"Up to {c.PerUserLimit} times per user");
        list.Add("Completed bus trips in Dhaka (boarded and ended in the app)");
        return list;
    }

    // ---- Membership ----

    public static List<MembershipTier> Ordered(IEnumerable<MembershipTier> tiers) => tiers.OrderBy(t => t.Rank).ToList();

    public static int RankOf(IReadOnlyList<MembershipTier> tiers, string? code) =>
        tiers.FirstOrDefault(t => t.Code == code)?.Rank ?? 0;

    public static string NameOf(IReadOnlyList<MembershipTier> tiers, string code) =>
        tiers.FirstOrDefault(t => t.Code == code)?.Name ?? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(code);

    public static bool Reaches(MembershipTier tier, int trips, decimal spend) =>
        trips >= tier.MinTrips || tier.MinSpend > 0 && spend >= tier.MinSpend;

    // The highest tier reached; the lowest tier when none is.
    public static MembershipTier TierFor(IReadOnlyList<MembershipTier> tiers, int trips, decimal spend)
    {
        var ordered = Ordered(tiers);
        var current = ordered[0];
        foreach (var tier in ordered)
        {
            if (Reaches(tier, trips, spend))
            {
                current = tier;
            }
        }
        return current;
    }

    public static TierProgress Progress(IReadOnlyList<MembershipTier> tiers, int trips, decimal spend)
    {
        var ordered = Ordered(tiers);
        var current = TierFor(ordered, trips, spend);
        var next = ordered.FirstOrDefault(t => t.Rank > current.Rank);
        if (next is null)
        {
            return new TierProgress(current, null, 0, 0, 100);
        }
        var tripsToNext = Math.Max(0, next.MinTrips - trips);
        var spendToNext = next.MinSpend > 0 ? Math.Max(0, next.MinSpend - spend) : 0;
        double ByTrips() => next.MinTrips <= current.MinTrips ? 1 : (double)(trips - current.MinTrips) / (next.MinTrips - current.MinTrips);
        double BySpend() => next.MinSpend <= 0 ? 0 : (double)((spend - current.MinSpend) / Math.Max(0.01m, next.MinSpend - current.MinSpend));
        var percent = (int)Math.Floor(Math.Clamp(Math.Max(ByTrips(), BySpend()), 0, 0.99) * 100);
        return new TierProgress(current, next, tripsToNext, spendToNext, percent);
    }

    // ---- Referral codes ----

    // No 0/O or 1/I, so a code read aloud or copied by hand is not mistyped.
    private const string ReferralAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string NewReferralCode() => "DHK" + RandomNumberGenerator.GetString(ReferralAlphabet, 6);

    [GeneratedRegex("^DHK[A-Z0-9]{6}$")]
    private static partial Regex ReferralRegex();

    public static string? NormalizeReferralCode(string? code)
    {
        var text = code?.Trim().ToUpperInvariant().Replace(" ", "").Replace("-", "") ?? "";
        return ReferralRegex().IsMatch(text) ? text : null;
    }

    // Why this user cannot be referred with this code, or null.
    public static string? ReferralProblem(Guid userId, Guid referrerId, bool alreadyReferred, bool referrerWasReferredByUser,
        DateTime accountCreatedUtc, DateTime nowUtc, int eligibleTrips, RewardSettings settings)
    {
        if (!settings.ReferralEnabled)
        {
            return "Refer & Earn is turned off right now.";
        }
        if (referrerId == userId)
        {
            return "You cannot use your own referral code.";
        }
        if (alreadyReferred)
        {
            return "You have already used a referral code.";
        }
        if (referrerWasReferredByUser)
        {
            return "You invited this person, so you cannot use their code.";
        }
        if (eligibleTrips > 0)
        {
            return "Referral codes must be entered before your first eligible trip.";
        }
        if (nowUtc - accountCreatedUtc > TimeSpan.FromDays(settings.ReferralApplyDays))
        {
            return $"Referral codes can only be entered within {settings.ReferralApplyDays} days of signing up.";
        }
        return null;
    }
}
