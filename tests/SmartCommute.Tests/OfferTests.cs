using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Tests;

[Collection(ServerCollection.Name)]
public class OfferTests(TestServerFixture server)
{
    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<RewardOptions> Rewards()
    {
        await using var scope = server.Scope();
        return scope.ServiceProvider.GetRequiredService<OfferService>().Rewards;
    }

    private async Task<(Offer Offer, Wallet Wallet, List<OfferTransaction> Ledger, List<WalletTransaction> WalletLedger)> State(Guid userId)
    {
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Offers.AsNoTracking().SingleAsync(o => o.UserId == userId),
            await db.Wallets.AsNoTracking().SingleAsync(w => w.UserId == userId),
            await db.OfferTransactions.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(),
            await db.WalletTransactions.AsNoTracking().Where(x => x.Wallet!.UserId == userId).ToListAsync());
    }

    private async Task<Guid> CompletedTrip(Guid userId, decimal fare, string status = TripStatus.Completed)
    {
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var trip = new Trip { UserId = userId, BusName = "Test Paribahan", Source = "Mirpur 10", Destination = "Farmgate", Fare = fare, Status = status, EndedAt = DateTime.UtcNow };
        db.Trips.Add(trip);
        await db.SaveChangesAsync();
        return trip.Id;
    }

    private async Task<(User User, HttpClient Client)> Admin()
    {
        var (user, _) = await server.CreateUserAsync();
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await db.Users.SingleAsync(u => u.Id == user.Id);
        admin.Role = UserRoles.Admin;
        var session = new UserSession { UserId = admin.Id, CreatedAt = DateTime.UtcNow, ExpiresAt = scope.ServiceProvider.GetRequiredService<JwtTokenService>().NewExpiry() };
        db.UserSessions.Add(session);
        await db.SaveChangesAsync();
        return (admin, server.ClientFor(scope.ServiceProvider.GetRequiredService<JwtTokenService>().CreateToken(admin, session)));
    }

    private static Task<HttpResponseMessage> UseCashback(HttpClient client, object body) => client.PostAsJsonAsync("/offers/use-cashback", body);

    // ---- welcome offers ----

    [SkippableFact]
    public async Task Sign_up_gives_the_configured_welcome_offers_once()
    {
        RequireServer();
        var rewards = await Rewards();
        var email = $"{Guid.NewGuid():N}@test.local";
        var signup = await server.ClientFor(null).PostAsJsonAsync("/auth/signup", new { name = "Offer Rider", email, password = "Secret123" });
        Assert.Equal(HttpStatusCode.OK, signup.StatusCode);
        var body = await JsonOf(signup);
        var userId = Guid.Parse(body.GetProperty("user").GetProperty("id").GetString()!);

        var (offer, _, ledger, _) = await State(userId);
        Assert.Equal(rewards.WelcomeCashback, offer.Cashback);
        Assert.Equal(rewards.WelcomeCoupon, offer.Coupon);
        Assert.Equal(rewards.WelcomeDiscount, offer.Discount);
        Assert.All(ledger, x => Assert.Equal(OfferEntryReasons.Welcome, x.Reason));

        // Visiting the page again, even many times at once, adds nothing.
        var client = server.ClientFor(body.GetProperty("token").GetString());
        var visits = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.GetAsync("/offers/user")));
        Assert.All(visits, v => Assert.Equal(HttpStatusCode.OK, v.StatusCode));
        var page = await JsonOf(visits[0]);
        Assert.Equal(rewards.WelcomeCashback, page.GetProperty("cashback").GetDecimal());
        Assert.Equal(rewards.WelcomeCoupon, page.GetProperty("welcome").GetProperty("coupon").GetDecimal());
        Assert.Equal(ledger.Count, (await State(userId)).Ledger.Count);
    }

    [SkippableFact]
    public async Task Older_accounts_get_their_welcome_offers_once_even_on_simultaneous_first_visits()
    {
        RequireServer();
        var rewards = await Rewards();
        var (user, client) = await server.CreateUserAsync();
        var visits = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.GetAsync("/offers/user")));
        Assert.All(visits, v => Assert.Equal(HttpStatusCode.OK, v.StatusCode));

        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Offers.CountAsync(o => o.UserId == user.Id));
        Assert.Equal(rewards.WelcomeCashback, (await State(user.Id)).Offer.Cashback);
        Assert.Equal(1, await db.OfferTransactions.CountAsync(x => x.UserId == user.Id && x.Reference == "welcome:cashback"));
    }

    // ---- cashback ----

    [SkippableFact]
    public async Task Redeeming_cashback_moves_it_into_the_wallet_and_ledger()
    {
        RequireServer();
        var (user, client) = await server.CreateUserAsync(balance: 5);
        await client.GetAsync("/offers/user");
        var before = await State(user.Id);
        Assert.True(before.Offer.Cashback >= 2, "the welcome cashback must be at least ৳2 for this test");

        var response = await UseCashback(client, new { amount = 2m });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal(before.Offer.Cashback - 2, body.GetProperty("offers").GetProperty("cashback").GetDecimal());
        Assert.Equal(7m, body.GetProperty("wallet").GetProperty("balance").GetDecimal());
        Assert.Equal(2m, body.GetProperty("transaction").GetProperty("amount").GetDecimal());

        var after = await State(user.Id);
        Assert.Equal(before.Offer.Cashback - 2, after.Offer.Cashback);
        Assert.Equal(7m, after.Wallet.Balance);
        var credit = Assert.Single(after.WalletLedger, x => x.Type == OfferKinds.Cashback);
        Assert.Equal(2m, credit.Amount);
        var used = Assert.Single(after.Ledger, x => x.Reason == OfferEntryReasons.Redeemed);
        Assert.Equal(-2m, used.Amount);
        Assert.Equal(credit.Id, used.WalletTransactionId);
    }

    [SkippableFact]
    public async Task Cashback_cannot_go_beyond_what_is_left_or_take_bad_amounts()
    {
        RequireServer();
        var (user, client) = await server.CreateUserAsync();
        await client.GetAsync("/offers/user");
        var available = (await State(user.Id)).Offer.Cashback;

        foreach (var body in new object[] { new { amount = available + 1 }, new { amount = 0 }, new { amount = -5 }, new { amount = 1.005m } })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await UseCashback(client, body)).StatusCode);
        }
        var bad = await client.PostAsync("/offers/use-cashback", new StringContent("""{"amount":"lots"}""", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var state = await State(user.Id);
        Assert.Equal(available, state.Offer.Cashback);
        Assert.Equal(0m, state.Wallet.Balance);

        // {} redeems everything; after that there is nothing left.
        Assert.Equal(HttpStatusCode.OK, (await UseCashback(client, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UseCashback(client, new { })).StatusCode);
        Assert.Equal(available, (await State(user.Id)).Wallet.Balance);
    }

    [SkippableFact]
    public async Task A_repeated_reference_pays_once()
    {
        RequireServer();
        var (user, client) = await server.CreateUserAsync();
        await client.GetAsync("/offers/user");
        var reference = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => UseCashback(client, new { amount = 1m, reference })));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var state = await State(user.Id);
        Assert.Equal(1m, state.Wallet.Balance);
        Assert.Single(state.Ledger, x => x.Reference == reference);
    }

    [SkippableFact]
    public async Task Simultaneous_redemptions_never_overspend()
    {
        RequireServer();
        var (user, client) = await server.CreateUserAsync();
        await client.GetAsync("/offers/user");
        var available = (await State(user.Id)).Offer.Cashback;
        var each = Math.Floor(available / 2) + 1; // two of these cannot both fit

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => UseCashback(client, new { amount = each })));
        var paid = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(1, paid);
        var state = await State(user.Id);
        Assert.Equal(available - each, state.Offer.Cashback);
        Assert.Equal(each, state.Wallet.Balance);
        Assert.True(state.Offer.Cashback >= 0);
    }

    // ---- coupons and discounts on trips ----

    [SkippableFact]
    public async Task Coupon_and_discount_come_off_the_trip_fare_once_each()
    {
        RequireServer();
        var (user, client) = await server.CreateUserAsync();
        await client.GetAsync("/offers/user");
        var start = (await State(user.Id)).Offer;
        var fare = start.Coupon + start.Discount + 50; // more than both together, so both are used in full
        var trip = await CompletedTrip(user.Id, fare);

        var coupon = await client.PostAsJsonAsync("/offers/use-coupon", new { tripId = trip });
        Assert.Equal(HttpStatusCode.OK, coupon.StatusCode);
        var couponTrip = (await JsonOf(coupon)).GetProperty("trip");
        Assert.Equal(start.Coupon, couponTrip.GetProperty("saving").GetDecimal());
        Assert.Equal(fare - start.Coupon, couponTrip.GetProperty("finalFare").GetDecimal());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/offers/use-coupon", new { tripId = trip })).StatusCode);

        var discount = await client.PostAsJsonAsync("/offers/use-discount", new { tripId = trip });
        Assert.Equal(HttpStatusCode.OK, discount.StatusCode);
        Assert.Equal(fare - start.Coupon - start.Discount, (await JsonOf(discount)).GetProperty("trip").GetProperty("finalFare").GetDecimal());

        var state = await State(user.Id);
        Assert.Equal(0m, state.Offer.Coupon);
        Assert.Equal(0m, state.Offer.Discount);
        Assert.Equal(start.Coupon + start.Discount, state.Wallet.Balance);
        Assert.Equal(2, state.Ledger.Count(x => x.TripId == trip));

        // With nothing left, another trip cannot use them.
        var second = await CompletedTrip(user.Id, 30);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/offers/use-coupon", new { tripId = second })).StatusCode);
    }

    [SkippableFact]
    public async Task Offers_never_cover_more_than_the_fare()
    {
        RequireServer();
        var (user, client) = await server.CreateUserAsync();
        await client.GetAsync("/offers/user");
        var start = (await State(user.Id)).Offer;
        Assert.True(start.Coupon > 1, "the welcome coupon must be more than ৳1 for this test");
        var trip = await CompletedTrip(user.Id, 1);

        var response = await client.PostAsJsonAsync("/offers/use-coupon", new { tripId = trip });
        Assert.Equal(1m, (await JsonOf(response)).GetProperty("trip").GetProperty("saving").GetDecimal());
        Assert.Equal(start.Coupon - 1, (await State(user.Id)).Offer.Coupon);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/offers/use-discount", new { tripId = trip })).StatusCode);
    }

    [SkippableFact]
    public async Task Trips_must_be_your_own_and_completed()
    {
        RequireServer();
        var (user, client) = await server.CreateUserAsync();
        var (other, _) = await server.CreateUserAsync();
        var theirs = await CompletedTrip(other.Id, 40);
        var active = await CompletedTrip(user.Id, 40, TripStatus.Active);

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/offers/use-coupon", new { tripId = theirs })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/offers/use-coupon", new { tripId = active })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/offers/use-discount", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/offers/use-discount", new { tripId = Guid.NewGuid() })).StatusCode);
    }

    // ---- who may do what ----

    [SkippableFact]
    public async Task Users_cannot_see_others_offers_or_give_themselves_value()
    {
        RequireServer();
        var (user, client) = await server.CreateUserAsync();
        var (other, _) = await server.CreateUserAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/offers/user/{other.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/offers/user/{user.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/offers/add-cashback", new { amount = 500 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/offers/user", new { cashback = 9999, coupon = 0, discount = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.ClientFor(null).GetAsync("/offers/user")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await UseCashback(server.ClientFor(null), new { amount = 1 })).StatusCode);
    }

    [SkippableFact]
    public async Task Admins_grant_value_once_per_reference_and_their_changes_are_recorded()
    {
        RequireServer();
        var (admin, adminClient) = await Admin();
        var (user, client) = await server.CreateUserAsync();
        await client.GetAsync("/offers/user");
        var start = (await State(user.Id)).Offer;

        var body = new { userId = user.Id, amount = 25m, reference = "promo-2026-10" };
        Assert.Equal(HttpStatusCode.OK, (await adminClient.PostAsJsonAsync("/offers/add-cashback", body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await adminClient.PostAsJsonAsync("/offers/add-cashback", body)).StatusCode);
        Assert.Equal(start.Cashback + 25, (await State(user.Id)).Offer.Cashback);
        Assert.Equal(HttpStatusCode.NotFound, (await adminClient.PostAsJsonAsync("/offers/add-coupon", new { userId = Guid.NewGuid(), amount = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await adminClient.PostAsJsonAsync("/offers/add-coupon", new { userId = user.Id, amount = -5 })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await adminClient.PutAsJsonAsync($"/offers/user/{user.Id}", new { cashback = 0, coupon = 3, discount = 4, isActive = false })).StatusCode);
        var state = await State(user.Id);
        Assert.Equal(3m, state.Offer.Coupon);
        Assert.False(state.Offer.IsActive);
        Assert.Contains(state.Ledger, x => x.Reason == OfferEntryReasons.Adjusted && x.CreatedById == admin.Id);
        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync($"/offers/user/{user.Id}")).StatusCode);

        // Inactive offers cannot be used.
        var trip = await CompletedTrip(user.Id, 40);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/offers/use-coupon", new { tripId = trip })).StatusCode);
    }
}
