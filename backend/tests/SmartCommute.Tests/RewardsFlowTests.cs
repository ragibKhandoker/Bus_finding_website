using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Tests;

// F15 end to end against a real PostgreSQL database (skipped without SMARTCOMMUTE_TEST_DB): cashback from the
// end-trip workflow, coupons (limits, expiry and races for the last coupon), referrals, membership and who may call
// what.
[Collection(ServerCollection.Name)]
public class RewardsFlowTests(TestServerFixture server)
{
    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static string Code(string prefix) => (prefix + Guid.NewGuid().ToString("N"))[..12].ToUpperInvariant();

    // A completed trip as the end-trip workflow leaves it: boarded on a seeded vehicle, fare worked out by the server.
    private async Task<Guid> EligibleTripAsync(Guid userId, decimal fare = 100, bool boarded = true)
    {
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var vehicle = await db.IndividualBuses.Include(v => v.Bus).OrderBy(v => v.BusCode).FirstAsync();
        var trip = new Trip
        {
            UserId = userId, IndividualBusId = boarded ? vehicle.Id : null, BusId = vehicle.BusId.ToString(), BusName = vehicle.Bus!.BusName,
            RouteNumber = vehicle.Bus.RouteNumber, Source = "A", Destination = "B", Fare = fare, Status = TripStatus.Completed, EndedAt = DateTime.UtcNow,
        };
        db.Trips.Add(trip);
        await db.SaveChangesAsync();
        return trip.Id;
    }

    private async Task<TripRewardOutcome> ProcessAsync(Guid tripId)
    {
        await using var scope = server.Scope();
        return await scope.ServiceProvider.GetRequiredService<RewardEngine>().ProcessTripAsync(tripId);
    }

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> read)
    {
        await using var scope = server.Scope();
        return await read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<string> CreateCouponAsync(HttpClient admin, object body)
    {
        var response = await admin.PostAsJsonAsync("/admin/offers", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonOf(response)).GetProperty("data").GetProperty("code").GetString()!;
    }

    [SkippableFact]
    public async Task Ending_a_boarded_trip_credits_cashback_once()
    {
        RequireServer();
        var (user, _) = await server.CreateUserAsync();
        var tripId = await EligibleTripAsync(user.Id, fare: 100);

        var first = await ProcessAsync(tripId);
        Assert.True(first.Processed);
        Assert.True(first.Eligible);
        Assert.Equal(5m, first.Cashback); // 5% of ৳100 with the default settings

        // The same trip event again (a retry, the background job) gives nothing more.
        var again = await ProcessAsync(tripId);
        Assert.False(again.Processed);
        var lots = await DbAsync(db => db.CashbackTransactions.CountAsync(c => c.UserId == user.Id && c.EventKey == $"trip:{tripId}"));
        Assert.Equal(1, lots);
        var ledger = await DbAsync(db => db.OfferTransactions.CountAsync(x => x.UserId == user.Id && x.Reason == OfferEntryReasons.Earned));
        Assert.Equal(1, ledger);
    }

    [SkippableFact]
    public async Task Simultaneous_processing_of_one_trip_pays_once()
    {
        RequireServer();
        var (user, _) = await server.CreateUserAsync();
        var tripId = await EligibleTripAsync(user.Id, fare: 80);
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => ProcessAsync(tripId)));
        Assert.Single(results, r => r.Processed);
        Assert.Equal(1, await DbAsync(db => db.TripRewards.CountAsync(r => r.TripId == tripId)));
        Assert.Equal(4m, await DbAsync(db => db.CashbackTransactions.Where(c => c.EventKey == $"trip:{tripId}").SumAsync(c => c.Amount)));
    }

    [SkippableFact]
    public async Task Trips_typed_in_by_hand_earn_nothing()
    {
        RequireServer();
        var (user, _) = await server.CreateUserAsync();
        var tripId = await EligibleTripAsync(user.Id, fare: 500, boarded: false);
        var outcome = await ProcessAsync(tripId);
        Assert.False(outcome.Eligible);
        Assert.Equal(0m, outcome.Cashback);
        Assert.Equal(0, await DbAsync(db => db.CashbackTransactions.CountAsync(c => c.UserId == user.Id)));
    }

    [SkippableFact]
    public async Task Cashback_moves_into_the_wallet_with_a_ledger_entry()
    {
        RequireServer();
        var (user, client) = await server.CreateUserAsync();
        await ProcessAsync(await EligibleTripAsync(user.Id, fare: 100));
        var before = (await JsonOf(await client.GetAsync("/offers/cashback"))).GetProperty("data");
        Assert.True(before.GetProperty("totalEarned").GetDecimal() >= 5);

        var move = await client.PostAsJsonAsync("/offers/use-cashback", new { amount = 5, reference = Guid.NewGuid().ToString() });
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        var after = (await JsonOf(await client.GetAsync("/offers/cashback"))).GetProperty("data");
        Assert.Equal(5m, after.GetProperty("totalUsed").GetDecimal());
        var ledger = await server.LedgerOfAsync(user.Id);
        Assert.Contains(ledger, t => t.Type == OfferKinds.Cashback && t.Amount == 5);
    }

    [SkippableFact]
    public async Task Valid_coupons_save_money_once_and_bad_ones_are_rejected()
    {
        RequireServer();
        var (_, admin) = await server.CreateAdminAsync();
        var (user, client) = await server.CreateUserAsync();
        var code = await CreateCouponAsync(admin, new { code = Code("TEN"), title = "Ten percent", type = "percentage", discountType = "percent", discountValue = 10, minFare = 50, maxDiscount = 20, perUserLimit = 1 });
        var trip = await EligibleTripAsync(user.Id, fare: 300);
        await ProcessAsync(trip);

        var check = await JsonOf(await client.PostAsJsonAsync("/offers/validate-coupon", new { code, tripId = trip }));
        Assert.True(check.GetProperty("valid").GetBoolean());
        Assert.Equal(20m, check.GetProperty("data").GetProperty("discount").GetDecimal()); // 10% of 300, capped at 20

        // An amount sent by the client is ignored: the saving is the server's.
        var redeem = await client.PostAsJsonAsync("/offers/redeem-coupon", new { code, tripId = trip, reference = Guid.NewGuid().ToString(), discount = 999 });
        Assert.Equal(HttpStatusCode.OK, redeem.StatusCode);
        Assert.Equal(20m, (await JsonOf(redeem)).GetProperty("data").GetProperty("discount").GetDecimal());
        Assert.Equal(20m, (await server.WalletOfAsync(user.Id)).Balance);

        var second = await EligibleTripAsync(user.Id, fare: 300);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/offers/redeem-coupon", new { code, tripId = second })).StatusCode); // per-user limit

        var expired = await CreateCouponAsync(admin, new { code = Code("OLD"), title = "Old one", type = "fixed", discountType = "fixed", discountValue = 5,
            startsAt = DateTime.UtcNow.AddDays(-3).ToString("O"), expiresAt = DateTime.UtcNow.AddMinutes(-1).ToString("O") });
        var oldCheck = await JsonOf(await client.PostAsJsonAsync("/offers/validate-coupon", new { code = expired, tripId = second }));
        Assert.False(oldCheck.GetProperty("valid").GetBoolean());
        Assert.Contains("expired", oldCheck.GetProperty("message").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/offers/validate-coupon", new { code = "NOSUCHCODE1", tripId = second })).StatusCode);
        var lowFare = await EligibleTripAsync(user.Id, fare: 30);
        var minFare = await CreateCouponAsync(admin, new { code = Code("MIN"), title = "Needs 50", type = "fixed", discountType = "fixed", discountValue = 5, minFare = 50 });
        Assert.False((await JsonOf(await client.PostAsJsonAsync("/offers/validate-coupon", new { code = minFare, tripId = lowFare }))).GetProperty("valid").GetBoolean());
    }

    [SkippableFact]
    public async Task Many_users_racing_for_the_last_coupons_never_exceed_the_limit()
    {
        RequireServer();
        var (_, admin) = await server.CreateAdminAsync();
        var code = await CreateCouponAsync(admin, new { code = Code("LAST"), title = "Only three", type = "fixed", discountType = "fixed", discountValue = 5, totalLimit = 3 });
        var riders = new List<(HttpClient Client, Guid Trip)>();
        for (var i = 0; i < 8; i++)
        {
            var (user, client) = await server.CreateUserAsync();
            riders.Add((client, await EligibleTripAsync(user.Id, fare: 50)));
        }
        var responses = await Task.WhenAll(riders.Select(r => r.Client.PostAsJsonAsync("/offers/redeem-coupon", new { code, tripId = r.Trip, reference = Guid.NewGuid().ToString() })));
        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(3, await DbAsync(db => db.Coupons.Where(c => c.Code == code).Select(c => c.RedemptionCount).SingleAsync()));
        Assert.Equal(3, await DbAsync(db => db.CouponRedemptions.CountAsync(r => r.Coupon!.Code == code)));
    }

    [SkippableFact]
    public async Task One_user_cannot_beat_the_per_user_limit_with_simultaneous_requests()
    {
        RequireServer();
        var (_, admin) = await server.CreateAdminAsync();
        var (user, client) = await server.CreateUserAsync();
        var code = await CreateCouponAsync(admin, new { code = Code("ONCE"), title = "Once each", type = "fixed", discountType = "fixed", discountValue = 5, perUserLimit = 1 });
        var trips = new List<Guid>();
        for (var i = 0; i < 4; i++) trips.Add(await EligibleTripAsync(user.Id, fare: 40));
        var responses = await Task.WhenAll(trips.Select(t => client.PostAsJsonAsync("/offers/redeem-coupon", new { code, tripId = t, reference = Guid.NewGuid().ToString() })));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(5m, (await server.WalletOfAsync(user.Id)).Balance);
    }

    [SkippableFact]
    public async Task Referral_rewards_wait_for_the_first_eligible_trip()
    {
        RequireServer();
        var (inviter, inviterClient) = await server.CreateUserAsync();
        var (friend, friendClient) = await server.CreateUserAsync();
        var code = (await JsonOf(await inviterClient.GetAsync("/referrals/my-code"))).GetProperty("code").GetString()!;

        Assert.Equal(HttpStatusCode.Conflict, (await inviterClient.PostAsJsonAsync("/referrals/apply", new { code })).StatusCode); // own code
        Assert.Equal(HttpStatusCode.Created, (await friendClient.PostAsJsonAsync("/referrals/apply", new { code })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await friendClient.PostAsJsonAsync("/referrals/apply", new { code })).StatusCode); // twice

        Assert.Equal(0, await DbAsync(db => db.CashbackTransactions.CountAsync(c => c.Source == CashbackSources.Referral && (c.UserId == inviter.Id || c.UserId == friend.Id))));
        var outcome = await ProcessAsync(await EligibleTripAsync(friend.Id, fare: 60));
        Assert.True(outcome.ReferralRewarded);
        Assert.Equal(25m, await DbAsync(db => db.CashbackTransactions.Where(c => c.UserId == inviter.Id && c.Source == CashbackSources.Referral).SumAsync(c => c.Amount)));
        Assert.Equal(15m, await DbAsync(db => db.CashbackTransactions.Where(c => c.UserId == friend.Id && c.Source == CashbackSources.Referral).SumAsync(c => c.Amount)));
        Assert.Equal(ReferralStatus.Rewarded, await DbAsync(db => db.Referrals.Where(r => r.ReferredUserId == friend.Id).Select(r => r.Status).SingleAsync()));

        // A second trip does not reward the referral again.
        Assert.False((await ProcessAsync(await EligibleTripAsync(friend.Id, fare: 60))).ReferralRewarded);
    }

    [SkippableFact]
    public async Task Membership_moves_up_as_eligible_trips_are_completed()
    {
        RequireServer();
        var (user, client) = await server.CreateUserAsync();
        for (var i = 0; i < 10; i++)
        {
            await ProcessAsync(await EligibleTripAsync(user.Id, fare: 30));
        }
        var my = (await JsonOf(await client.GetAsync("/membership/my"))).GetProperty("data");
        Assert.Equal("silver", my.GetProperty("tier").GetProperty("code").GetString());
        Assert.Equal(10, my.GetProperty("eligibleTrips").GetInt32());
        Assert.Equal(15, my.GetProperty("tripsToNext").GetInt32());
        Assert.Equal(1, await DbAsync(db => db.Notifications.CountAsync(n => n.UserId == user.Id && n.Title == "You're now Silver")));
    }

    [SkippableFact]
    public async Task Only_admins_manage_offers_and_nobody_credits_cashback_by_hand()
    {
        RequireServer();
        var (user, client) = await server.CreateUserAsync();
        var anonymous = server.ClientFor(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/admin/offers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/offers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/admin/offers", new { code = "HACK10", title = "Free", discountValue = 100 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/admin/rewards/settings", new { cashbackPercent = 100 })).StatusCode);
        var trip = await EligibleTripAsync(user.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/offers/cashback/credit", new { tripId = trip, amount = 1000 })).StatusCode);
        Assert.Equal(0, await DbAsync(db => db.CashbackTransactions.CountAsync(c => c.UserId == user.Id)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/offers/cashback/history")).StatusCode);
    }

    [SkippableFact]
    public async Task Users_only_ever_see_their_own_rewards()
    {
        RequireServer();
        var (owner, _) = await server.CreateUserAsync();
        var (_, other) = await server.CreateUserAsync();
        var trip = await EligibleTripAsync(owner.Id);
        await ProcessAsync(trip);
        var history = await JsonOf(await other.GetAsync("/offers/cashback/history"));
        Assert.Equal(0, history.GetProperty("total").GetInt32());
        // Another user's trip cannot be used for a coupon.
        var (_, admin) = await server.CreateAdminAsync();
        var code = await CreateCouponAsync(admin, new { code = Code("MINE"), title = "Mine only", type = "fixed", discountType = "fixed", discountValue = 5 });
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync("/offers/redeem-coupon", new { code, tripId = trip })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/offers/user/{owner.Id}")).StatusCode);
    }

    [SkippableFact]
    public async Task Paused_campaigns_give_no_new_rewards()
    {
        RequireServer();
        var (_, admin) = await server.CreateAdminAsync();
        var (user, _) = await server.CreateUserAsync();
        var created = await JsonOf(await admin.PostAsJsonAsync("/admin/campaigns", new
        {
            name = "Test boost " + Guid.NewGuid().ToString("N")[..6], type = "cashback", startsAt = DateTime.UtcNow.AddMinutes(-5).ToString("O"),
            endsAt = DateTime.UtcNow.AddDays(1).ToString("O"), cashbackFixed = 3, budget = 100,
        }));
        var id = created.GetProperty("data").GetProperty("id").GetString();
        Assert.Equal(8m, (await ProcessAsync(await EligibleTripAsync(user.Id, fare: 100))).Cashback); // 5 + 3

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/admin/campaigns/{id}/status", new { status = "paused" })).StatusCode);
        Assert.Equal(5m, (await ProcessAsync(await EligibleTripAsync(user.Id, fare: 100))).Cashback);
        Assert.True(await DbAsync(db => db.RewardAuditLogs.AnyAsync(a => a.Action == "campaign:paused" && a.EntityId == id)));
    }
}
