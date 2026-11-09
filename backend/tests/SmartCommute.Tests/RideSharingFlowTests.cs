using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Tests;

// The advanced ride share flows end to end: seats and lifecycle, passenger management, chat, reviews, My Rides,
// discovery filters, reports and blocks.
[Collection(ServerCollection.Name)]
public class RideSharingFlowTests(TestServerFixture server)
{
    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static string Unique(string name) => $"{name} {Guid.NewGuid():N}"[..28];

    private static async Task<string> CreateRide(HttpClient host, Dictionary<string, object?> body)
    {
        var response = await host.PostAsJsonAsync("/api/rideshare", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonOf(response)).GetProperty("data").GetProperty("_id").GetString()!;
    }

    private static async Task<string> Join(HttpClient rider, string rideId)
    {
        var response = await rider.PostAsJsonAsync($"/api/rideshare/{rideId}/requests", new { note = "At the gate" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonOf(response)).GetProperty("data").GetProperty("_id").GetString()!;
    }

    private static Task<HttpResponseMessage> Approve(HttpClient host, string requestId) => host.PostAsync($"/api/rideshare/requests/{requestId}/approve", null);

    private static async Task<JsonElement> Ride(HttpClient client, string rideId) => (await JsonOf(await client.GetAsync($"/api/rideshare/{rideId}"))).GetProperty("data");

    private async Task<int> Notifications(Guid userId, string title)
    {
        await using var scope = server.Scope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Notifications.CountAsync(n => n.UserId == userId && n.Title == title);
    }

    [SkippableFact]
    public async Task A_ride_fills_up_and_opens_again_when_a_seat_is_released()
    {
        RequireServer();
        var (host, hostClient) = await server.CreateUserAsync();
        var (rider, riderClient) = await server.CreateUserAsync();
        var (other, otherClient) = await server.CreateUserAsync();
        var rideId = await CreateRide(hostClient, RideTestData.Body(Unique("Banani"), maxParticipants: 3));

        Assert.Equal(HttpStatusCode.BadRequest, (await hostClient.PostAsJsonAsync($"/api/rideshare/{rideId}/requests", new { })).StatusCode);
        var first = await Join(riderClient, rideId);
        Assert.Equal(HttpStatusCode.Conflict, (await riderClient.PostAsJsonAsync($"/api/rideshare/{rideId}/requests", new { })).StatusCode);
        var second = await Join(otherClient, rideId);
        Assert.Equal(1, await Notifications(host.Id, "New ride request"));

        Assert.Equal(HttpStatusCode.Forbidden, (await Approve(riderClient, second)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Approve(hostClient, first)).StatusCode);
        Assert.Equal(1, (await Ride(hostClient, rideId)).GetProperty("seatsLeft").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await Approve(hostClient, second)).StatusCode);
        var full = await Ride(hostClient, rideId);
        Assert.Equal("FULL", full.GetProperty("state").GetString());
        Assert.Equal(0, full.GetProperty("seatsLeft").GetInt32());

        // A full ride leaves discovery; when the rider cancels their seat it is OPEN again.
        var list = await JsonOf(await server.ClientFor(null).GetAsync($"/api/rideshare?destination={Uri.EscapeDataString(full.GetProperty("destination").GetString()!)}"));
        Assert.Empty(list.GetProperty("data").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await otherClient.DeleteAsync($"/api/rideshare/requests/{first}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await riderClient.DeleteAsync($"/api/rideshare/requests/{first}")).StatusCode);
        Assert.Equal("OPEN", (await Ride(hostClient, rideId)).GetProperty("state").GetString());
        Assert.Equal(1, await Notifications(host.Id, "Rider left your ride"));

        // The host removes the other approved rider.
        Assert.Equal(HttpStatusCode.Forbidden, (await riderClient.DeleteAsync($"/api/rideshare/{rideId}/passengers/{other.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await hostClient.DeleteAsync($"/api/rideshare/{rideId}/passengers/{other.Id}")).StatusCode);
        Assert.Equal(1, await Notifications(other.Id, "Removed from ride"));
        Assert.Equal(2, (await Ride(hostClient, rideId)).GetProperty("seatsLeft").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await hostClient.DeleteAsync($"/api/rideshare/{rideId}/passengers/{rider.Id}")).StatusCode);
    }

    [SkippableFact]
    public async Task Only_the_host_and_approved_riders_use_the_chat_and_see_private_details()
    {
        RequireServer();
        var (_, hostClient) = await server.CreateUserAsync();
        var (_, riderClient) = await server.CreateUserAsync();
        var (_, pendingClient) = await server.CreateUserAsync();
        var rideId = await CreateRide(hostClient, RideTestData.Body(Unique("Gulshan")).With("pickupDetails", "Gate 2").With("contactPreference", "both"));
        Assert.Equal(HttpStatusCode.OK, (await Approve(hostClient, await Join(riderClient, rideId))).StatusCode);
        await Join(pendingClient, rideId);

        Assert.Equal(HttpStatusCode.Created, (await hostClient.PostAsJsonAsync($"/api/rideshare/{rideId}/messages", new { text = "Meet at 6" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await riderClient.PostAsJsonAsync($"/api/rideshare/{rideId}/messages", new { text = "On my way" })).StatusCode);
        var messages = (await JsonOf(await riderClient.GetAsync($"/api/rideshare/{rideId}/messages"))).GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(["Meet at 6", "On my way"], messages.Select(m => m.GetProperty("text").GetString()));
        Assert.True(messages[0].GetProperty("fromHost").GetBoolean());

        Assert.Equal(HttpStatusCode.Forbidden, (await pendingClient.GetAsync($"/api/rideshare/{rideId}/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await pendingClient.PostAsJsonAsync($"/api/rideshare/{rideId}/messages", new { text = "hi" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.ClientFor(null).GetAsync($"/api/rideshare/{rideId}/messages")).StatusCode);

        Assert.Equal("Gate 2", (await Ride(riderClient, rideId)).GetProperty("pickupDetails").GetString());
        Assert.Equal(JsonValueKind.Null, (await Ride(pendingClient, rideId)).GetProperty("pickupDetails").ValueKind);
        Assert.Equal(JsonValueKind.Null, (await Ride(pendingClient, rideId)).GetProperty("contact").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, (await pendingClient.GetAsync($"/api/rideshare/{rideId}/passengers")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await riderClient.GetAsync($"/api/rideshare/{rideId}/passengers")).StatusCode);
    }

    [SkippableFact]
    public async Task Members_review_each_other_once_after_completion()
    {
        RequireServer();
        var (host, hostClient) = await server.CreateUserAsync();
        var (rider, riderClient) = await server.CreateUserAsync();
        var (_, outsiderClient) = await server.CreateUserAsync();
        var rideId = await CreateRide(hostClient, RideTestData.Body(Unique("Uttara")));
        Assert.Equal(HttpStatusCode.OK, (await Approve(hostClient, await Join(riderClient, rideId))).StatusCode);

        object Review(Guid target, int stars) => new { reviewedUserId = target, rating = stars, comment = "Safe and on time" };
        Assert.Equal(HttpStatusCode.Conflict, (await riderClient.PostAsJsonAsync($"/api/rideshare/{rideId}/reviews", Review(host.Id, 5))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsiderClient.PostAsync($"/api/rideshare/{rideId}/complete", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await hostClient.PostAsync($"/api/rideshare/{rideId}/complete", null)).StatusCode);
        Assert.Equal(1, await Notifications(rider.Id, "Ride completed"));

        Assert.Equal(HttpStatusCode.Created, (await riderClient.PostAsJsonAsync($"/api/rideshare/{rideId}/reviews", Review(host.Id, 4))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await riderClient.PostAsJsonAsync($"/api/rideshare/{rideId}/reviews", Review(host.Id, 5))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await riderClient.PostAsJsonAsync($"/api/rideshare/{rideId}/reviews", Review(rider.Id, 5))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsiderClient.PostAsJsonAsync($"/api/rideshare/{rideId}/reviews", Review(host.Id, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await hostClient.PostAsJsonAsync($"/api/rideshare/{rideId}/reviews", Review(rider.Id, 6))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await hostClient.PostAsJsonAsync($"/api/rideshare/{rideId}/reviews", Review(rider.Id, 5))).StatusCode);

        var profile = (await JsonOf(await server.ClientFor(null).GetAsync($"/api/rideshare/users/{host.Id}/profile"))).GetProperty("data");
        Assert.Equal(4, profile.GetProperty("rating").GetDouble());
        Assert.Equal(1, profile.GetProperty("reviewCount").GetInt32());
        Assert.Equal(1, profile.GetProperty("completedRides").GetInt32());
        Assert.False(profile.TryGetProperty("email", out _));
        Assert.False(profile.TryGetProperty("phone", out _));
    }

    [SkippableFact]
    public async Task Cancelled_and_expired_rides_leave_discovery_and_close_requests()
    {
        RequireServer();
        var (host, hostClient) = await server.CreateUserAsync();
        var (rider, riderClient) = await server.CreateUserAsync();
        var (_, otherClient) = await server.CreateUserAsync();
        var place = Unique("Badda");
        var cancelled = await CreateRide(hostClient, RideTestData.Body(place));
        var expiring = await CreateRide(hostClient, RideTestData.Body(place));
        var pending = await Join(riderClient, cancelled);
        await Join(riderClient, expiring);

        Assert.Equal(HttpStatusCode.Forbidden, (await otherClient.PostAsJsonAsync($"/api/rideshare/{cancelled}/cancel", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await hostClient.PostAsJsonAsync($"/api/rideshare/{cancelled}/cancel", new { reason = "Car broke down" })).StatusCode);
        Assert.Equal(1, await Notifications(rider.Id, "Ride cancelled"));
        Assert.Equal(HttpStatusCode.Conflict, (await Approve(hostClient, pending)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await hostClient.PutAsJsonAsync($"/api/rideshare/{cancelled}", RideTestData.Body(place))).StatusCode);

        await using (var scope = server.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.RidePosts.Where(p => p.Id == Guid.Parse(expiring)).ExecuteUpdateAsync(s => s.SetProperty(p => p.DepartureAt, DateTime.UtcNow.AddMinutes(-5)));
            await scope.ServiceProvider.GetRequiredService<RideLifecycle>().RunOnceAsync(DateTime.UtcNow);
            Assert.Equal(RideStatus.Expired, (await db.RidePosts.AsNoTracking().SingleAsync(p => p.Id == Guid.Parse(expiring))).Status);
            Assert.All(await db.RideRequests.AsNoTracking().Where(r => r.RequesterId == rider.Id).ToListAsync(), r => Assert.Equal(RideRequestStatus.Cancelled, r.Status));
        }
        var open = await JsonOf(await server.ClientFor(null).GetAsync($"/api/rideshare?destination={Uri.EscapeDataString(place)}"));
        Assert.Empty(open.GetProperty("data").EnumerateArray());

        // Both stay in the host's history and in the rider's joined list.
        var hosted = (await JsonOf(await hostClient.GetAsync("/api/rideshare/my/hosted"))).GetProperty("data").EnumerateArray()
            .ToDictionary(r => r.GetProperty("_id").GetString()!, r => r.GetProperty("state").GetString());
        Assert.Equal("CANCELLED", hosted[cancelled]);
        Assert.Equal("EXPIRED", hosted[expiring]);
        var joined = (await JsonOf(await riderClient.GetAsync("/api/rideshare/my/joined"))).GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(2, joined.Count);
        Assert.All(joined, j => Assert.Equal("CANCELLED", j.GetProperty("request").GetProperty("state").GetString()));
        Assert.Equal(1, await Notifications(host.Id, "Ride expired"));
    }

    [SkippableFact]
    public async Task Discovery_filters_and_sorts_real_rides()
    {
        RequireServer();
        var (_, hostClient) = await server.CreateUserAsync();
        var place = Unique("Dhanmondi");
        var cheapCng = await CreateRide(hostClient, RideTestData.Body(place, vehicle: "CNG", cost: 40, maxParticipants: 4, leavesIn: TimeSpan.FromHours(5)));
        var car = await CreateRide(hostClient, RideTestData.Body(place, vehicle: "Car", cost: 120, maxParticipants: 5, leavesIn: TimeSpan.FromHours(2)));

        async Task<List<string>> Ids(string query) => (await JsonOf(await server.ClientFor(null).GetAsync($"/api/rideshare?destination={Uri.EscapeDataString(place)}&{query}")))
            .GetProperty("data").EnumerateArray().Select(r => r.GetProperty("_id").GetString()!).ToList();
        Assert.Equal([cheapCng], await Ids("vehicle=CNG"));
        Assert.Equal([car], await Ids("minCost=100&maxCost=200"));
        Assert.Equal([car], await Ids("minSeats=4"));
        Assert.Equal([cheapCng, car], await Ids("sort=cost"));
        Assert.Equal([car, cheapCng], await Ids("sort=departure"));
        Assert.Equal([car, cheapCng], await Ids("sort=seats"));
        Assert.Empty(await Ids("gender=Female"));
        Assert.Equal(HttpStatusCode.BadRequest, (await server.ClientFor(null).GetAsync("/api/rideshare?vehicle=Plane")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await server.ClientFor(null).GetAsync("/api/rideshare?minCost=50&maxCost=10")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await server.ClientFor(null).GetAsync("/api/rideshare?date=10-11-2026")).StatusCode);
    }

    [SkippableFact]
    public async Task Reports_reach_admins_and_blocks_keep_people_apart()
    {
        RequireServer();
        var (host, hostClient) = await server.CreateUserAsync();
        var (rider, riderClient) = await server.CreateUserAsync();
        var (admin, adminClient) = await server.CreateAdminAsync();
        var place = Unique("Mohakhali");
        var rideId = await CreateRide(hostClient, RideTestData.Body(place));

        Assert.Equal(HttpStatusCode.BadRequest, (await hostClient.PostAsJsonAsync($"/api/rideshare/{rideId}/report", new { reason = "spam" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await riderClient.PostAsJsonAsync($"/api/rideshare/{rideId}/report", new { reason = "rude" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await riderClient.PostAsJsonAsync($"/api/rideshare/{rideId}/report", new { reason = "fake", description = "Not a real ride" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await riderClient.PostAsJsonAsync($"/api/rideshare/{rideId}/report", new { reason = "fake" })).StatusCode);
        Assert.True(await Notifications(admin.Id, "New ride share report") >= 1);

        var reports = (await JsonOf(await adminClient.GetAsync("/admin/ride-reports?status=open"))).GetProperty("data").EnumerateArray().ToList();
        var report = reports.First(r => r.GetProperty("ridePostId").GetString() == rideId);
        Assert.Equal(HttpStatusCode.Forbidden, (await riderClient.GetAsync("/admin/ride-reports")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await adminClient.PostAsJsonAsync($"/admin/ride-reports/{report.GetProperty("_id").GetString()}/resolve",
            new { status = "reviewed", cancelRide = true })).StatusCode);
        Assert.Equal("CANCELLED", (await Ride(hostClient, rideId)).GetProperty("state").GetString());

        // A blocked rider no longer sees the host's rides and cannot join them.
        var another = await CreateRide(hostClient, RideTestData.Body(place));
        Assert.Equal(HttpStatusCode.OK, (await hostClient.PostAsync($"/api/rideshare/users/{rider.Id}/block", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await riderClient.PostAsJsonAsync($"/api/rideshare/{another}/requests", new { })).StatusCode);
        var seen = await JsonOf(await riderClient.GetAsync($"/api/rideshare?destination={Uri.EscapeDataString(place)}"));
        Assert.Empty(seen.GetProperty("data").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await hostClient.PostAsync($"/api/rideshare/users/{host.Id}/block", null)).StatusCode);
    }
}
