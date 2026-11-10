using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Tests;

// F15: runs the reward queries against a database that cannot be reached (see RideQueryShapeTests). EF Core
// translates a query before it connects, so a query it cannot translate fails with its own error, while a good one
// only fails on the connection. The settings and tiers are put in the memory cache first, so each method gets as
// far as its own queries.
public class PromotionQueryShapeTests
{
    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1")
        .UseSnakeCaseNamingConvention().Options);

    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Trip = Guid.NewGuid();

    private sealed record Services(AppDbContext Db, RewardConfigService Config, OfferService Offers, CouponService Coupons, CashbackService Cashback,
        MembershipService Membership, RewardEngine Engine);

    private static Services Make(AppDbContext db)
    {
        var config = new RewardConfigService(db, Options.Create(new PromotionOptions()), new MemoryCache(new MemoryCacheOptions()));
        config.Remember(PromotionRulesTests.Settings(), PromotionRulesTests.Tiers());
        var wallets = new WalletService(db, Options.Create(new RewardOptions()));
        var notifications = new NotificationService(db);
        var offers = new OfferService(db, wallets, notifications);
        var coupons = new CouponService(db, wallets, notifications, config);
        var cashback = new CashbackService(db, offers, notifications, config);
        var membership = new MembershipService(db, config);
        var engine = new RewardEngine(db, offers, cashback, coupons, config, notifications, NullLogger<RewardEngine>.Instance);
        return new(db, config, offers, coupons, cashback, membership, engine);
    }

    public static TheoryData<string> Queries => new()
    {
        "coupon-list", "eligible-trips", "quote", "best", "cashback-summary", "cashback-history", "cashback-expiry", "membership",
        "campaigns", "referral-summary", "referral-history", "analytics", "summary", "admin-stats", "admin-redemptions",
        "admin-suspicious", "admin-referrals", "catch-up", "lifecycle", "process-trip", "auto-issued",
    };

    private static Task Run(string name, Services s) => name switch
    {
        "coupon-list" => s.Coupons.ListAsync(Me, DateTime.UtcNow),
        "eligible-trips" => s.Coupons.EligibleTripsAsync(Me, DateTime.UtcNow),
        "quote" => s.Coupons.QuoteAsync(Me, "DHAKA10", Trip, DateTime.UtcNow),
        "best" => s.Coupons.BestAsync(Me, Trip, DateTime.UtcNow),
        "cashback-summary" => s.Cashback.SummaryAsync(Me, DateTime.UtcNow),
        "cashback-history" => s.Cashback.HistoryAsync(Me, 1, 20),
        "cashback-expiry" => s.Cashback.ExpireDueAsync(DateTime.UtcNow),
        "membership" => s.Membership.MyAsync(Me),
        "campaigns" => new CampaignService(s.Db, s.Config).ActiveAsync(Me, DateTime.UtcNow),
        "referral-summary" => new ReferralService(s.Db, s.Config, new NotificationService(s.Db)).SummaryAsync(Me),
        "referral-history" => new ReferralService(s.Db, s.Config, new NotificationService(s.Db)).HistoryAsync(Me),
        "analytics" => new RewardInsightsService(s.Db, s.Coupons, s.Membership).AnalyticsAsync(Me, DateTime.UtcNow),
        "summary" => new RewardInsightsService(s.Db, s.Coupons, s.Membership).SummaryAsync(Me, DateTime.UtcNow),
        "admin-stats" => Admin(s).StatisticsAsync(DateTime.UtcNow),
        "admin-redemptions" => Admin(s).RedemptionsAsync("DHAKA10", 1, 25),
        "admin-suspicious" => Admin(s).SuspiciousAsync(DateTime.UtcNow),
        "admin-referrals" => Admin(s).ReferralsAsync(null),
        "catch-up" => s.Engine.CatchUpAsync(DateTime.UtcNow),
        "lifecycle" => new PromotionLifecycle(s.Db, s.Cashback, s.Engine, new NotificationService(s.Db)).RunOnceAsync(DateTime.UtcNow),
        "process-trip" => s.Engine.ProcessTripAsync(Trip),
        "auto-issued" => s.Coupons.AutoIssuedAsync(CouponTypes.Welcome, DateTime.UtcNow),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static PromotionAdminService Admin(Services s) => new(s.Db, s.Config, s.Coupons, new NotificationService(s.Db));

    [Theory]
    [MemberData(nameof(Queries))]
    public async Task Query_is_valid_and_only_fails_on_the_missing_database(string name)
    {
        await using var db = Db();
        var error = await Record.ExceptionAsync(() => Run(name, Make(db)));
        Assert.NotNull(error);
        Assert.True(error is not InvalidOperationException || error.Message.Contains("transient failure"), $"{name}: {error.Message} | {error.InnerException?.Message}");
    }
}
