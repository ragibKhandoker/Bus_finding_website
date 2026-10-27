using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Tests;

[Collection(ServerCollection.Name)]
public class RideRequestTests(TestServerFixture server)
{
    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string> CreateRide(HttpClient host, int maxParticipants = 4)
    {
        var response = await host.PostAsJsonAsync("/api/rideshare", new { source = "Mirpur 10", destination = $"Motijheel {Guid.NewGuid():N}"[..20], maxParticipants, genderPreference = "Any" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonOf(response)).GetProperty("data").GetProperty("_id").GetString()!;
    }

    private static Task<HttpResponseMessage> Join(HttpClient rider, string rideId) =>
        rider.PostAsJsonAsync("/api/riderequests", new { ridePostId = rideId });

    private static async Task<string> RequestId(HttpClient rider, string rideId)
    {
        var response = await Join(rider, rideId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonOf(response)).GetProperty("data").GetProperty("_id").GetString()!;
    }

    private static Task<HttpResponseMessage> Accept(HttpClient host, string requestId) => host.PutAsync($"/api/riderequests/accept/{requestId}", null);
    private static Task<HttpResponseMessage> Reject(HttpClient host, string requestId) => host.PutAsync($"/api/riderequests/reject/{requestId}", null);

    private async Task<int> Notifications(Guid userId, string title)
    {
        await using var scope = server.Scope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Notifications.CountAsync(n => n.UserId == userId && n.Title == title);
    }

    private async Task<List<Guid>> Participants(string rideId)
    {
        await using var scope = server.Scope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().RideRequests
            .Where(r => r.RidePostId == Guid.Parse(rideId) && r.Status == RideRequestStatus.Accepted)
            .Select(r => r.RequesterId).ToListAsync();
    }

    // ---- sending ----

    [SkippableFact]
    public async Task A_rider_sends_a_pending_request_as_themselves()
    {
        RequireServer();
        var (host, hostClient) = await server.CreateUserAsync();
        var (rider, riderClient) = await server.CreateUserAsync();
        var rideId = await CreateRide(hostClient);

        var response = await riderClient.PostAsJsonAsync("/api/riderequests", new { ridePostId = rideId, requesterId = host.Id });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var request = (await JsonOf(response)).GetProperty("data");
        Assert.Equal("pending", request.GetProperty("status").GetString());
        Assert.Equal(rider.Id.ToString(), request.GetProperty("requesterId").GetString());
        Assert.Equal(rideId, request.GetProperty("ridePostId").GetString());
        Assert.False(request.GetProperty("requester").TryGetProperty("email", out _));
        Assert.Equal(1, await Notifications(host.Id, "New ride request"));

        // The older /send path does the same thing, and refuses the duplicate.
        Assert.Equal(HttpStatusCode.Conflict, (await riderClient.PostAsJsonAsync("/api/riderequests/send", new { ridePostId = rideId })).StatusCode);
    }

    [SkippableFact]
    public async Task Invalid_join_requests_are_refused()
    {
        RequireServer();
        var (_, hostClient) = await server.CreateUserAsync();
        var (_, riderClient) = await server.CreateUserAsync();
        var rideId = await CreateRide(hostClient);
        var doneId = await CreateRide(hostClient);
        Assert.Equal(HttpStatusCode.OK, (await hostClient.PostAsJsonAsync("/api/rideshare/complete", new { postId = doneId })).StatusCode);

        await RequestId(riderClient, rideId);
        Assert.Equal(HttpStatusCode.Conflict, (await Join(riderClient, rideId)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Join(hostClient, rideId)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Join(riderClient, doneId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Join(riderClient, Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await riderClient.PostAsJsonAsync("/api/riderequests", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await riderClient.PostAsJsonAsync("/api/riderequests", new { ridePostId = "not-a-guid" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Join(server.ClientFor(null), rideId)).StatusCode);
    }

    [SkippableFact]
    public async Task Simultaneous_joins_by_one_rider_make_one_request()
    {
        RequireServer();
        var (_, hostClient) = await server.CreateUserAsync();
        var (rider, riderClient) = await server.CreateUserAsync();
        var rideId = await CreateRide(hostClient);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Join(riderClient, rideId)));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));
        await using var scope = server.Scope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().RideRequests.CountAsync(r => r.RequesterId == rider.Id));
    }

    // ---- answering ----

    [SkippableFact]
    public async Task Host_accepts_once_and_the_rider_is_told()
    {
        RequireServer();
        var (host, hostClient) = await server.CreateUserAsync();
        var (rider, riderClient) = await server.CreateUserAsync();
        var (_, outsiderClient) = await server.CreateUserAsync();
        var rideId = await CreateRide(hostClient);
        var requestId = await RequestId(riderClient, rideId);

        Assert.Equal(HttpStatusCode.Forbidden, (await Accept(outsiderClient, requestId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Accept(riderClient, requestId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Reject(outsiderClient, requestId)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Accept(server.ClientFor(null), requestId)).StatusCode);

        var accept = await Accept(hostClient, requestId);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        var body = await JsonOf(accept);
        Assert.Equal("accepted", body.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(2, body.GetProperty("ride").GetProperty("participantCount").GetInt32());
        Assert.Equal(2, body.GetProperty("ride").GetProperty("seatsLeft").GetInt32());

        // Repeats change nothing: no second participant, no second notification.
        Assert.Equal(HttpStatusCode.Conflict, (await Accept(hostClient, requestId)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Reject(hostClient, requestId)).StatusCode);
        Assert.Equal([rider.Id], await Participants(rideId));
        Assert.Equal(1, await Notifications(rider.Id, "Ride request accepted"));
        Assert.Equal(0, await Notifications(host.Id, "Ride request accepted"));

        // An accepted rider cannot ask again.
        var again = await Join(riderClient, rideId);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("You have already joined this ride.", (await JsonOf(again)).GetProperty("message").GetString());
    }

    [SkippableFact]
    public async Task Rejected_rider_is_told_is_not_added_and_may_ask_again()
    {
        RequireServer();
        var (host, hostClient) = await server.CreateUserAsync();
        var (rider, riderClient) = await server.CreateUserAsync();
        var rideId = await CreateRide(hostClient);
        var requestId = await RequestId(riderClient, rideId);

        var reject = await Reject(hostClient, requestId);
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        Assert.Equal("rejected", (await JsonOf(reject)).GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Reject(hostClient, requestId)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Accept(hostClient, requestId)).StatusCode);
        Assert.Empty(await Participants(rideId));
        Assert.Equal(1, await Notifications(rider.Id, "Ride request declined"));
        Assert.Equal(0, await Notifications(host.Id, "Ride request declined"));

        Assert.Equal(HttpStatusCode.Created, (await Join(riderClient, rideId)).StatusCode);
    }

    [SkippableFact]
    public async Task A_full_ride_accepts_and_takes_no_more_riders()
    {
        RequireServer();
        var (_, hostClient) = await server.CreateUserAsync();
        var (_, firstClient) = await server.CreateUserAsync();
        var (_, secondClient) = await server.CreateUserAsync();
        var (_, lateClient) = await server.CreateUserAsync();
        var rideId = await CreateRide(hostClient, maxParticipants: 2);
        var first = await RequestId(firstClient, rideId);
        var second = await RequestId(secondClient, rideId);

        Assert.Equal(HttpStatusCode.OK, (await Accept(hostClient, first)).StatusCode);
        var blocked = await Accept(hostClient, second);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains("No places are left", (await JsonOf(blocked)).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Join(lateClient, rideId)).StatusCode);
        Assert.Single(await Participants(rideId));
    }

    [SkippableFact]
    public async Task Simultaneous_accepts_never_overfill_a_ride()
    {
        RequireServer();
        var (_, hostClient) = await server.CreateUserAsync();
        var rideId = await CreateRide(hostClient, maxParticipants: 3);
        var requestIds = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            var (_, riderClient) = await server.CreateUserAsync();
            requestIds.Add(await RequestId(riderClient, rideId));
        }

        var responses = await Task.WhenAll(requestIds.Select(id => Accept(hostClient, id)));
        Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        Assert.Equal(2, (await Participants(rideId)).Count);
    }

    // ---- reading ----

    [SkippableFact]
    public async Task Request_lists_are_private_and_show_the_ride()
    {
        RequireServer();
        var (_, hostClient) = await server.CreateUserAsync();
        var (rider, riderClient) = await server.CreateUserAsync();
        var (_, outsiderClient) = await server.CreateUserAsync();
        var rideId = await CreateRide(hostClient);
        await RequestId(riderClient, rideId);

        var forRide = await hostClient.GetAsync($"/api/riderequests/ride/{rideId}");
        Assert.Equal(HttpStatusCode.OK, forRide.StatusCode);
        var rideBody = await JsonOf(forRide);
        Assert.Single(rideBody.GetProperty("data").EnumerateArray());
        Assert.Equal(3, rideBody.GetProperty("ride").GetProperty("seatsLeft").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await outsiderClient.GetAsync($"/api/riderequests/ride/{rideId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await hostClient.GetAsync($"/api/riderequests/ride/{Guid.NewGuid()}")).StatusCode);

        var mine = await riderClient.GetAsync($"/api/riderequests/user/{rider.Id}");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        var item = Assert.Single((await JsonOf(mine)).GetProperty("data").EnumerateArray());
        Assert.Equal("pending", item.GetProperty("status").GetString());
        Assert.Equal(rideId, item.GetProperty("ridePost").GetProperty("_id").GetString());
        Assert.Equal("Mirpur 10", item.GetProperty("ridePost").GetProperty("source").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await outsiderClient.GetAsync($"/api/riderequests/user/{rider.Id}")).StatusCode);
    }
}
