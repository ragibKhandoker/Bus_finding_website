using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Tests;

// F15: the reward rules without a database. Every amount here is what the server would credit or take off.
public class PromotionRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 6, 0, 0, DateTimeKind.Utc); // Monday 12:00 in Dhaka
    private static readonly DateTime Friday = new(2026, 11, 13, 6, 0, 0, DateTimeKind.Utc);

    public static RewardSettings Settings(Action<RewardSettings>? change = null)
    {
        var s = new RewardSettings
        {
            CashbackEnabled = true, CashbackPercent = 5, CashbackFixed = 0, CashbackMinFare = 20, CashbackMaxPerTrip = 10,
            CashbackMaxPerUserMonthly = 300, CashbackExpiryDays = 90, AllowCouponStacking = false, AllowCouponWithCashback = true,
            NewUserDays = 30, FirstTripCashback = 10, ReferralEnabled = true, ReferrerReward = 25, ReferredReward = 15, ReferralApplyDays = 7,
            MaxReferralRewardsPerMonth = 20, StartedAt = Now.AddDays(-30),
        };
        change?.Invoke(s);
        return s;
    }

    public static List<MembershipTier> Tiers() =>
    [
        new() { Code = "bronze", Name = "Bronze", Rank = 0, MinTrips = 0 },
        new() { Code = "silver", Name = "Silver", Rank = 1, MinTrips = 10, ExtraCashbackPercent = 1, ExtraCapPerTrip = 2, ExtraMonthlyCap = 50 },
        new() { Code = "gold", Name = "Gold", Rank = 2, MinTrips = 25, ExtraCashbackPercent = 2, ExtraCapPerTrip = 5, ExtraMonthlyCap = 100 },
        new() { Code = "platinum", Name = "Platinum", Rank = 3, MinTrips = 50, MinSpend = 5000, ExtraCashbackPercent = 3, ExtraCapPerTrip = 10, ExtraMonthlyCap = 200 },
    ];

    private static Trip BoardedTrip(decimal fare = 100, DateTime? ended = null) => new()
    {
        Status = TripStatus.Completed, IndividualBusId = Guid.NewGuid(), Fare = fare, EndedAt = ended ?? Now, RouteNumber = "8",
    };

    private static Coupon Dhaka10(Action<Coupon>? change = null)
    {
        var c = new Coupon
        {
            Id = Guid.NewGuid(), Code = "DHAKA10", Title = "10% off", DiscountType = PromoDiscountTypes.Percent, DiscountValue = 10, MinFare = 50,
            MaxDiscount = 20, StartsAt = Now.AddDays(-1), ExpiresAt = Now.AddDays(30), PerUserLimit = 1, IsActive = true, Audience = CouponAudiences.Everyone,
        };
        change?.Invoke(c);
        return c;
    }

    private static CouponContext Ctx(decimal fare = 100, Action<RewardSettings>? settings = null, int used = 0, decimal covered = 0,
        DateTime? ended = null, string? route = "8", UserCoupon? copy = null, int tierRank = 0, bool isNew = false, bool otherCoupon = false, bool cashback = false) =>
        new(Now, fare, covered, ended ?? Now, route, used, copy, tierRank, isNew, otherCoupon, cashback, Settings(settings), Tiers());

    // ---- Cashback ----

    [Fact]
    public void Eligible_trip_earns_the_configured_percentage()
    {
        var quote = PromotionRules.Cashback(100, Settings(), Tiers()[0], null, 0);
        Assert.Equal(5m, quote.Total);
        Assert.Equal(5m, quote.BaseAmount);
    }

    [Fact]
    public void Fixed_and_percentage_cashback_add_up()
    {
        var quote = PromotionRules.Cashback(60, Settings(s => { s.CashbackPercent = 5; s.CashbackFixed = 2; }), null, null, 0);
        Assert.Equal(5m, quote.Total); // 3 + 2
    }

    [Fact]
    public void Trips_entered_by_hand_never_qualify()
    {
        var logged = new Trip { Status = TripStatus.Completed, Fare = 100, EndedAt = Now };
        Assert.NotNull(PromotionRules.TripProblem(logged, Now.AddDays(-1)));
        Assert.Null(PromotionRules.TripProblem(BoardedTrip(), Now.AddDays(-1)));
    }

    [Fact]
    public void Unfinished_free_or_old_trips_do_not_qualify()
    {
        Assert.NotNull(PromotionRules.TripProblem(BoardedTrip(), Now.AddDays(1)));                     // ended before rewards started
        var active = BoardedTrip(); active.Status = TripStatus.Active;
        Assert.NotNull(PromotionRules.TripProblem(active, Now.AddDays(-1)));
        Assert.NotNull(PromotionRules.TripProblem(BoardedTrip(fare: 0), Now.AddDays(-1)));
    }

    [Fact]
    public void Below_the_minimum_fare_there_is_no_cashback()
    {
        Assert.Equal(0m, PromotionRules.Cashback(19, Settings(), null, null, 0).Total);
        Assert.True(PromotionRules.Cashback(20, Settings(), null, null, 0).Total > 0);
    }

    [Fact]
    public void Per_trip_cap_is_respected()
    {
        var quote = PromotionRules.Cashback(1000, Settings(), Tiers()[0], null, 0);
        Assert.Equal(10m, quote.Total);
        Assert.Equal("per-trip", quote.LimitedBy);
    }

    [Fact]
    public void Monthly_user_cap_is_respected()
    {
        Assert.Equal(2m, PromotionRules.Cashback(100, Settings(), Tiers()[0], null, earnedThisMonth: 298).Total);
        Assert.Equal(0m, PromotionRules.Cashback(100, Settings(), Tiers()[0], null, earnedThisMonth: 300).Total);
    }

    [Fact]
    public void A_zero_limit_means_no_limit()
    {
        var quote = PromotionRules.Cashback(1000, Settings(s => { s.CashbackMaxPerTrip = 0; s.CashbackMaxPerUserMonthly = 0; }), null, null, 10_000);
        Assert.Equal(50m, quote.Total);
    }

    [Fact]
    public void Tier_benefits_add_cashback_and_raise_the_cap()
    {
        var gold = Tiers()[2];
        var quote = PromotionRules.Cashback(100, Settings(), gold, null, 0);
        Assert.Equal(5m, quote.BaseAmount);
        Assert.Equal(2m, quote.TierBonus);
        Assert.Equal(7m, quote.Total);
        // The cap is 10 + 5 for Gold.
        Assert.Equal(15m, PromotionRules.Cashback(1000, Settings(), gold, null, 0).Total);
    }

    [Fact]
    public void Campaign_cashback_is_added_within_its_own_cap_and_budget()
    {
        var campaign = new RewardCampaign { CashbackPercent = 5, MaxCashbackPerTrip = 3, MinFare = 20 };
        Assert.Equal(8m, PromotionRules.Cashback(100, Settings(), null, campaign, 0).Total);           // 5 + min(5, 3)
        Assert.Equal(6m, PromotionRules.Cashback(100, Settings(), null, campaign, 0, campaignBudgetLeft: 1).Total);
    }

    [Fact]
    public void Cashback_is_rounded_down_to_whole_poisha_and_never_more_than_the_fare()
    {
        Assert.Equal(1.21m, PromotionRules.Cashback(24.3m, Settings(), null, null, 0).Total); // 1.215 -> 1.21
        var generous = Settings(s => { s.CashbackPercent = 100; s.CashbackFixed = 50; s.CashbackMaxPerTrip = 0; });
        Assert.Equal(30m, PromotionRules.Cashback(30, generous, null, null, 0).Total);
    }

    [Fact]
    public void Cashback_can_be_switched_off()
    {
        Assert.Equal(0m, PromotionRules.Cashback(100, Settings(s => s.CashbackEnabled = false), Tiers()[2], null, 0).Total);
    }

    [Fact]
    public void Paused_expired_and_spent_campaigns_do_not_apply()
    {
        var tiers = Tiers();
        var live = new RewardCampaign { Status = CampaignStatus.Active, StartsAt = Now.AddDays(-1), EndsAt = Now.AddDays(1), CashbackPercent = 5 };
        Assert.Null(PromotionRules.CampaignProblem(live, Now, Now, 100, 0, tiers, false, 3));
        Assert.NotNull(PromotionRules.CampaignProblem(new RewardCampaign { Status = CampaignStatus.Paused, StartsAt = live.StartsAt, EndsAt = live.EndsAt }, Now, Now, 100, 0, tiers, false, 3));
        Assert.NotNull(PromotionRules.CampaignProblem(new RewardCampaign { Status = CampaignStatus.Active, StartsAt = Now.AddDays(-3), EndsAt = Now.AddDays(-1) }, Now, Now, 100, 0, tiers, false, 3));
        Assert.NotNull(PromotionRules.CampaignProblem(new RewardCampaign { Status = CampaignStatus.Active, StartsAt = live.StartsAt, EndsAt = live.EndsAt, Budget = 100, Spent = 100 }, Now, Now, 100, 0, tiers, false, 3));
    }

    [Fact]
    public void Campaign_conditions_are_checked()
    {
        var tiers = Tiers();
        RewardCampaign C(Action<RewardCampaign> change) { var c = new RewardCampaign { Status = CampaignStatus.Active, StartsAt = Now.AddDays(-1), EndsAt = Now.AddDays(5) }; change(c); return c; }
        Assert.NotNull(PromotionRules.CampaignProblem(C(c => c.WeekendOnly = true), Now, Now, 100, 0, tiers, false, 3));
        Assert.Null(PromotionRules.CampaignProblem(C(c => c.WeekendOnly = true), Now, Friday, 100, 0, tiers, false, 3));
        Assert.NotNull(PromotionRules.CampaignProblem(C(c => c.MinTier = "gold"), Now, Now, 100, 1, tiers, false, 3));
        Assert.Null(PromotionRules.CampaignProblem(C(c => c.MinTier = "gold"), Now, Now, 100, 2, tiers, false, 3));
        Assert.NotNull(PromotionRules.CampaignProblem(C(c => c.NewUsersOnly = true), Now, Now, 100, 0, tiers, false, 3));
        Assert.NotNull(PromotionRules.CampaignProblem(C(c => c.FirstTripOnly = true), Now, Now, 100, 0, tiers, true, 1));
        Assert.NotNull(PromotionRules.CampaignProblem(C(c => c.MinFare = 150), Now, Now, 100, 0, tiers, false, 3));
    }

    [Fact]
    public void The_weekend_is_Friday_and_Saturday_in_Dhaka_time()
    {
        // Thursday 19:00 UTC is already Friday 01:00 in Dhaka.
        Assert.True(PromotionRules.IsWeekend(new DateTime(2026, 11, 12, 19, 0, 0, DateTimeKind.Utc)));
        Assert.False(PromotionRules.IsWeekend(new DateTime(2026, 11, 12, 17, 0, 0, DateTimeKind.Utc)));
        Assert.True(PromotionRules.IsWeekend(new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc)));
        Assert.False(PromotionRules.IsWeekend(new DateTime(2026, 11, 14, 18, 30, 0, DateTimeKind.Utc))); // Sunday 00:30 Dhaka
    }

    // ---- Coupons ----

    [Fact]
    public void Valid_coupon_is_accepted_with_the_right_saving()
    {
        var check = PromotionRules.Check(Dhaka10(), null, Ctx(fare: 100));
        Assert.True(check.Ok, check.Error);
        Assert.Equal(10m, check.Discount);
        Assert.Equal(90m, check.FinalFare);
    }

    [Fact]
    public void Expired_or_not_started_coupons_are_rejected()
    {
        Assert.Contains("expired", PromotionRules.Check(Dhaka10(c => c.ExpiresAt = Now.AddMinutes(-1)), null, Ctx()).Error);
        Assert.Contains("from", PromotionRules.Check(Dhaka10(c => c.StartsAt = Now.AddDays(1)), null, Ctx()).Error);
        Assert.False(PromotionRules.Check(Dhaka10(c => c.IsActive = false), null, Ctx()).Ok);
        Assert.False(PromotionRules.Check(Dhaka10(c => c.DeletedAt = Now), null, Ctx()).Ok);
    }

    [Fact]
    public void Invalid_codes_are_rejected_before_any_lookup()
    {
        Assert.Null(PromotionRules.NormalizeCode(""));
        Assert.Null(PromotionRules.NormalizeCode("A"));
        Assert.Null(PromotionRules.NormalizeCode("DROP TABLE;"));
        Assert.Equal("DHAKA10", PromotionRules.NormalizeCode(" dhaka 10 "));
    }

    [Fact]
    public void Minimum_fare_is_enforced()
    {
        var check = PromotionRules.Check(Dhaka10(), null, Ctx(fare: 49));
        Assert.False(check.Ok);
        Assert.Contains("at least ৳50", check.Error);
    }

    [Fact]
    public void Maximum_discount_is_enforced()
    {
        Assert.Equal(20m, PromotionRules.Check(Dhaka10(), null, Ctx(fare: 400)).Discount);
    }

    [Fact]
    public void Per_user_limit_is_enforced()
    {
        Assert.Contains("already used", PromotionRules.Check(Dhaka10(), null, Ctx(used: 1)).Error);
        var twice = Dhaka10(c => c.PerUserLimit = 2);
        Assert.True(PromotionRules.Check(twice, null, Ctx(used: 1)).Ok);
        Assert.False(PromotionRules.Check(twice, null, Ctx(used: 2)).Ok);
    }

    [Fact]
    public void Global_redemption_limit_is_enforced()
    {
        Assert.Contains("fully redeemed", PromotionRules.Check(Dhaka10(c => { c.TotalLimit = 5; c.RedemptionCount = 5; }), null, Ctx()).Error);
        Assert.True(PromotionRules.Check(Dhaka10(c => { c.TotalLimit = 5; c.RedemptionCount = 4; }), null, Ctx()).Ok);
    }

    [Fact]
    public void The_final_fare_never_goes_below_zero()
    {
        var big = Dhaka10(c => { c.DiscountType = PromoDiscountTypes.Fixed; c.DiscountValue = 500; c.MaxDiscount = null; c.MinFare = 0; });
        var check = PromotionRules.Check(big, null, Ctx(fare: 30, covered: 12));
        Assert.Equal(18m, check.Discount);
        Assert.Equal(0m, check.FinalFare);
        Assert.False(PromotionRules.Check(big, null, Ctx(fare: 30, covered: 30)).Ok);
    }

    [Fact]
    public void Stacking_rules_follow_the_settings()
    {
        Assert.Contains("cannot be combined", PromotionRules.Check(Dhaka10(), null, Ctx(otherCoupon: true)).Error);
        Assert.True(PromotionRules.Check(Dhaka10(), null, Ctx(otherCoupon: true, settings: s => s.AllowCouponStacking = true)).Ok);
        Assert.True(PromotionRules.Check(Dhaka10(), null, Ctx(cashback: true)).Ok);
        Assert.Contains("cashback", PromotionRules.Check(Dhaka10(), null, Ctx(cashback: true, settings: s => s.AllowCouponWithCashback = false)).Error);
    }

    [Fact]
    public void Weekend_route_tier_and_new_user_coupons_check_their_condition()
    {
        Assert.False(PromotionRules.Check(Dhaka10(c => c.WeekendOnly = true), null, Ctx()).Ok);
        Assert.True(PromotionRules.Check(Dhaka10(c => c.WeekendOnly = true), null, Ctx(ended: Friday)).Ok);
        Assert.False(PromotionRules.Check(Dhaka10(c => c.RouteNumber = "36"), null, Ctx(route: "8")).Ok);
        Assert.True(PromotionRules.Check(Dhaka10(c => c.RouteNumber = "8"), null, Ctx(route: "8")).Ok);
        Assert.Contains("Gold", PromotionRules.Check(Dhaka10(c => c.MinTier = "gold"), null, Ctx(tierRank: 1)).Error);
        Assert.True(PromotionRules.Check(Dhaka10(c => c.MinTier = "gold"), null, Ctx(tierRank: 3)).Ok);
        Assert.False(PromotionRules.Check(Dhaka10(c => c.Audience = CouponAudiences.NewUsers), null, Ctx(isNew: false)).Ok);
        Assert.True(PromotionRules.Check(Dhaka10(c => c.Audience = CouponAudiences.NewUsers), null, Ctx(isNew: true)).Ok);
    }

    [Fact]
    public void Assigned_coupons_need_an_unused_unexpired_copy()
    {
        var welcome = Dhaka10(c => c.Audience = CouponAudiences.Assigned);
        Assert.Contains("not available", PromotionRules.Check(welcome, null, Ctx()).Error);
        Assert.True(PromotionRules.Check(welcome, null, Ctx(copy: new UserCoupon { ExpiresAt = Now.AddDays(3) })).Ok);
        Assert.Contains("expired", PromotionRules.Check(welcome, null, Ctx(copy: new UserCoupon { ExpiresAt = Now.AddMinutes(-1) })).Error);
        Assert.False(PromotionRules.Check(welcome, null, Ctx(copy: new UserCoupon { UsedAt = Now.AddDays(-1) })).Ok);
    }

    [Fact]
    public void A_coupon_of_a_paused_or_spent_campaign_cannot_be_used()
    {
        var campaign = new RewardCampaign { Status = CampaignStatus.Paused, StartsAt = Now.AddDays(-1), EndsAt = Now.AddDays(5) };
        Assert.Contains("paused", PromotionRules.Check(Dhaka10(), campaign, Ctx()).Error);
        campaign.Status = CampaignStatus.Active;
        campaign.Budget = 50;
        campaign.Spent = 46;
        Assert.Equal(4m, PromotionRules.Check(Dhaka10(), campaign, Ctx()).Discount); // only what is left of the budget
        campaign.Spent = 50;
        Assert.False(PromotionRules.Check(Dhaka10(), campaign, Ctx()).Ok);
    }

    // ---- Membership ----

    [Theory]
    [InlineData(0, "bronze")]
    [InlineData(9, "bronze")]
    [InlineData(10, "silver")]
    [InlineData(24, "silver")]
    [InlineData(25, "gold")]
    [InlineData(50, "platinum")]
    [InlineData(500, "platinum")]
    public void Correct_tier_is_assigned(int trips, string tier)
    {
        Assert.Equal(tier, PromotionRules.TierFor(Tiers(), trips, 0).Code);
    }

    [Fact]
    public void Spending_can_also_reach_a_tier()
    {
        Assert.Equal("platinum", PromotionRules.TierFor(Tiers(), 30, 5000).Code);
    }

    [Fact]
    public void Progress_is_calculated_towards_the_next_tier()
    {
        var p = PromotionRules.Progress(Tiers(), 5, 0);
        Assert.Equal("bronze", p.Current.Code);
        Assert.Equal("silver", p.Next!.Code);
        Assert.Equal(5, p.TripsToNext);
        Assert.Equal(50, p.Percent);

        var gold = PromotionRules.Progress(Tiers(), 30, 0);
        Assert.Equal("gold", gold.Current.Code);
        Assert.Equal(20, gold.TripsToNext);
        Assert.Equal(20, gold.Percent); // 5 of the 25 trips from Gold to Platinum

        var top = PromotionRules.Progress(Tiers(), 80, 0);
        Assert.Null(top.Next);
        Assert.Equal(100, top.Percent);
    }

    // ---- Referrals ----

    [Fact]
    public void Referral_codes_have_a_fixed_readable_format()
    {
        var code = PromotionRules.NewReferralCode();
        Assert.Matches("^DHK[A-HJ-NP-Z2-9]{6}$", code);
        Assert.Equal(code, PromotionRules.NormalizeReferralCode(" " + code.ToLowerInvariant().Insert(3, "-") + " "));
        Assert.Null(PromotionRules.NormalizeReferralCode("ABC"));
        Assert.Null(PromotionRules.NormalizeReferralCode("FRIEND"));
    }

    [Fact]
    public void Valid_referral_is_accepted()
    {
        Assert.Null(PromotionRules.ReferralProblem(Guid.NewGuid(), Guid.NewGuid(), false, false, Now.AddDays(-1), Now, 0, Settings()));
    }

    [Fact]
    public void Self_referral_is_rejected()
    {
        var me = Guid.NewGuid();
        Assert.Contains("own referral code", PromotionRules.ReferralProblem(me, me, false, false, Now.AddDays(-1), Now, 0, Settings()));
    }

    [Fact]
    public void Duplicate_and_mutual_referrals_are_rejected()
    {
        Assert.Contains("already used", PromotionRules.ReferralProblem(Guid.NewGuid(), Guid.NewGuid(), true, false, Now.AddDays(-1), Now, 0, Settings()));
        Assert.Contains("invited this person", PromotionRules.ReferralProblem(Guid.NewGuid(), Guid.NewGuid(), false, true, Now.AddDays(-1), Now, 0, Settings()));
    }

    [Fact]
    public void Referral_codes_must_come_before_the_first_trip_and_within_the_window()
    {
        Assert.NotNull(PromotionRules.ReferralProblem(Guid.NewGuid(), Guid.NewGuid(), false, false, Now.AddDays(-1), Now, 1, Settings()));
        Assert.NotNull(PromotionRules.ReferralProblem(Guid.NewGuid(), Guid.NewGuid(), false, false, Now.AddDays(-8), Now, 0, Settings()));
        Assert.NotNull(PromotionRules.ReferralProblem(Guid.NewGuid(), Guid.NewGuid(), false, false, Now.AddDays(-1), Now, 0, Settings(s => s.ReferralEnabled = false)));
    }

    [Fact]
    public void New_users_are_young_accounts_without_an_eligible_trip()
    {
        Assert.True(PromotionRules.IsNewUser(Now.AddDays(-3), Now, Settings(), 0));
        Assert.False(PromotionRules.IsNewUser(Now.AddDays(-3), Now, Settings(), 1));
        Assert.False(PromotionRules.IsNewUser(Now.AddDays(-31), Now, Settings(), 0));
    }

    [Fact]
    public void Month_start_is_the_Dhaka_month()
    {
        // 1 December 00:30 in Dhaka is still 30 November in UTC.
        var start = PromotionRules.MonthStartUtc(new DateTime(2026, 11, 30, 18, 30, 0, DateTimeKind.Utc));
        Assert.Equal(new DateTime(2026, 11, 30, 18, 0, 0, DateTimeKind.Utc), start);
    }
}
