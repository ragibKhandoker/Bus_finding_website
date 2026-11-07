using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Tests;

[Collection(ServerCollection.Name)]
public class RideShareTests(TestServerFixture server)
{
    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    // A destination nobody else in the test run uses, so lists can be checked exactly.
    private static string Place(string name) => $"{name} {Guid.NewGuid():N}"[..30];

    private static Task<HttpResponseMessage> Post(HttpClient client, object body) => client.PostAsJsonAsync("/api/rideshare", body);

    private static async Task<string> CreateRide(HttpClient client, string destination, string preference = "Any")
    {
        var response = await Post(client, new { source = "Mirpur 10", destination, maxParticipants = 3, genderPreference = preference });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonOf(response)).GetProperty("data").GetProperty("_id").GetString()!;
    }

    private async Task<List<string>> OpenIds(string destination)
    {
        var response = await server.ClientFor(null).GetAsync($"/api/rideshare?destination={Uri.EscapeDataString(destination)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await JsonOf(response)).GetProperty("data").EnumerateArray().Select(r => r.GetProperty("_id").GetString()!).ToList();
    }

    private static Task<HttpResponseMessage> Complete(HttpClient client, string postId) =>
        client.PostAsJsonAsync("/api/rideshare/complete", new { postId });

    // ---- creating ----

    [SkippableFact]
    public async Task Signed_in_user_creates_a_ride_as_themselves()
    {
        RequireServer();
        var (host, client) = await server.CreateUserAsync();
        var (other, _) = await server.CreateUserAsync();
        var destination = Place("Dhanmondi");

        var response = await Post(client, new { source = "  Mirpur 10 ", destination = $" {destination} ", maxParticipants = 3, genderPreference = "female", userId = other.Id, userName = "Someone Else" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ride = (await JsonOf(response)).GetProperty("data");
        Assert.Equal("Mirpur 10", ride.GetProperty("source").GetString());
        Assert.Equal(destination, ride.GetProperty("destination").GetString());
        Assert.Equal(3, ride.GetProperty("maxParticipants").GetInt32());
        Assert.Equal("Female", ride.GetProperty("genderPreference").GetString());
        Assert.Equal("active", ride.GetProperty("status").GetString());
        Assert.Equal(host.Id.ToString(), ride.GetProperty("host").GetProperty("id").GetString());
        Assert.Equal(host.Name, ride.GetProperty("host").GetProperty("name").GetString());
        Assert.False(ride.GetProperty("host").TryGetProperty("email", out _));

        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.RidePosts.AsNoTracking().SingleAsync(p => p.Id == Guid.Parse(ride.GetProperty("_id").GetString()!));
        Assert.Equal(host.Id, saved.UserId);
        Assert.Equal(RideStatus.Active, saved.Status);
        Assert.True(saved.CreatedAt > DateTime.UtcNow.AddMinutes(-1));
    }

    [SkippableFact]
    public async Task Guests_and_bad_tokens_cannot_create_delete_or_complete()
    {
        RequireServer();
        var (_, hostClient) = await server.CreateUserAsync();
        var postId = await CreateRide(hostClient, Place("Gulshan"));
        foreach (var client in new[] { server.ClientFor(null), server.ClientFor("not.a.token") })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, new { source = "A", destination = "B", maxParticipants = 2, genderPreference = "Any" })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync($"/api/rideshare/{postId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Complete(client, postId)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/riderequests/send", new { ridePostId = postId })).StatusCode);
        }
    }

    [SkippableFact]
    public async Task Invalid_ride_fields_are_refused()
    {
        RequireServer();
        var (_, client) = await server.CreateUserAsync();
        object[] invalid =
        [
            new { destination = "Banani", maxParticipants = 3, genderPreference = "Any" },
            new { source = "   ", destination = "Banani", maxParticipants = 3, genderPreference = "Any" },
            new { source = "Mirpur", destination = "", maxParticipants = 3, genderPreference = "Any" },
            new { source = "Mirpur", maxParticipants = 3, genderPreference = "Any" },
            new { source = "Mirpur", destination = "Banani", maxParticipants = 0, genderPreference = "Any" },
            new { source = "Mirpur", destination = "Banani", maxParticipants = -2, genderPreference = "Any" },
            new { source = "Mirpur", destination = "Banani", maxParticipants = 1, genderPreference = "Any" },
            new { source = "Mirpur", destination = "Banani", maxParticipants = 11, genderPreference = "Any" },
            new { source = "Mirpur", destination = "Banani", genderPreference = "Any" },
            new { source = "Mirpur", destination = "Banani", maxParticipants = 3, genderPreference = "Other" },
            new { source = "Mirpur", destination = "Banani", maxParticipants = 3 },
            new { source = new string('a', 201), destination = "Banani", maxParticipants = 3, genderPreference = "Any" },
        ];
        foreach (var body in invalid)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, body)).StatusCode);
        }
        var fraction = await client.PostAsync("/api/rideshare", new StringContent(
            """{"source":"Mirpur","destination":"Banani","maxParticipants":2.5,"genderPreference":"Any"}""", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, fraction.StatusCode);
    }

    // ---- browsing ----

    [SkippableFact]
    public async Task Destination_filter_ignores_case_and_surrounding_spaces()
    {
        RequireServer();
        var (_, client) = await server.CreateUserAsync();
        var destination = Place("Dhanmondi 27");
        var id = await CreateRide(client, destination);
        var elsewhere = await CreateRide(client, Place("Uttara"));

        Assert.Equal([id], await OpenIds(destination));
        Assert.Equal([id], await OpenIds($"   {destination.ToUpperInvariant()}  "));
        Assert.Equal([id], await OpenIds(destination[..20].ToLowerInvariant()));
        Assert.DoesNotContain(elsewhere, await OpenIds(destination));
        Assert.Empty(await OpenIds(Place("Nowhere")));
        Assert.Equal(HttpStatusCode.BadRequest, (await server.ClientFor(null).GetAsync($"/api/rideshare?destination={new string('x', 201)}")).StatusCode);
    }

    [SkippableFact]
    public async Task Users_see_all_their_own_rides_but_others_see_only_open_ones()
    {
        RequireServer();
        var (host, hostClient) = await server.CreateUserAsync();
        var (_, otherClient) = await server.CreateUserAsync();
        var open = await CreateRide(hostClient, Place("Banani"));
        var done = await CreateRide(hostClient, Place("Motijheel"));
        Assert.Equal(HttpStatusCode.OK, (await Complete(hostClient, done)).StatusCode);

        async Task<List<string>> Ids(HttpClient c) => (await JsonOf(await c.GetAsync($"/api/rideshare/user/{host.Id}")))
            .GetProperty("data").EnumerateArray().Select(r => r.GetProperty("_id").GetString()!).ToList();
        Assert.Equal([done, open], await Ids(hostClient));
        Assert.Equal([open], await Ids(otherClient));
        Assert.Equal([open], await Ids(server.ClientFor(null)));
    }

    // ---- deleting and completing ----

    [SkippableFact]
    public async Task Only_the_host_can_delete_a_ride()
    {
        RequireServer();
        var (_, hostClient) = await server.CreateUserAsync();
        var (_, otherClient) = await server.CreateUserAsync();
        var destination = Place("Mohakhali");
        var postId = await CreateRide(hostClient, destination);

        Assert.Equal(HttpStatusCode.Forbidden, (await otherClient.DeleteAsync($"/api/rideshare/{postId}")).StatusCode);
        Assert.Equal([postId], await OpenIds(destination));

        Assert.Equal(HttpStatusCode.OK, (await hostClient.DeleteAsync($"/api/rideshare/{postId}")).StatusCode);
        Assert.Empty(await OpenIds(destination));
        Assert.Equal(HttpStatusCode.NotFound, (await hostClient.DeleteAsync($"/api/rideshare/{postId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Complete(hostClient, postId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await hostClient.DeleteAsync($"/api/rideshare/{Guid.NewGuid()}")).StatusCode);

        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.NotNull((await db.RidePosts.AsNoTracking().SingleAsync(p => p.Id == Guid.Parse(postId))).DeletedAt);
    }

    [SkippableFact]
    public async Task Only_the_host_can_complete_and_completed_rides_leave_the_open_list()
    {
        RequireServer();
        var (host, hostClient) = await server.CreateUserAsync();
        var (_, otherClient) = await server.CreateUserAsync();
        var destination = Place("Farmgate");
        var postId = await CreateRide(hostClient, destination);
        var gemsBefore = (await server.WalletOfAsync(host.Id)).Gems;

        Assert.Equal(HttpStatusCode.Forbidden, (await Complete(otherClient, postId)).StatusCode);
        Assert.Equal([postId], await OpenIds(destination));

        var complete = await Complete(hostClient, postId);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        var ride = (await JsonOf(complete)).GetProperty("data");
        Assert.Equal("completed", ride.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, ride.GetProperty("completedAt").ValueKind);
        Assert.Empty(await OpenIds(destination));

        // Completing again changes nothing and pays no second reward.
        var gemsAfter = (await server.WalletOfAsync(host.Id)).Gems;
        Assert.Equal(HttpStatusCode.OK, (await Complete(hostClient, postId)).StatusCode);
        Assert.Equal(gemsAfter, (await server.WalletOfAsync(host.Id)).Gems);
        Assert.True(gemsAfter > gemsBefore);
        Assert.Equal(HttpStatusCode.BadRequest, (await hostClient.PostAsJsonAsync("/api/rideshare/complete", new { })).StatusCode);
    }

    [SkippableFact]
    public async Task Simultaneous_completes_reward_once()
    {
        RequireServer();
        var (host, hostClient) = await server.CreateUserAsync();
        var postId = await CreateRide(hostClient, Place("Shahbag"));
        var before = (await server.WalletOfAsync(host.Id)).Gems;

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Complete(hostClient, postId)));
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        Assert.Equal(before + 10, (await server.WalletOfAsync(host.Id)).Gems);
    }

    // ---- join requests (existing feature, now tied to the signed-in user) ----

    [SkippableFact]
    public async Task Join_requests_use_the_signed_in_user_and_respect_the_gender_preference()
    {
        RequireServer();
        var (host, hostClient) = await server.CreateUserAsync();
        var (rider, riderClient) = await server.CreateUserAsync();
        var (_, outsiderClient) = await server.CreateUserAsync();
        var postId = await CreateRide(hostClient, Place("Badda"));
        var femaleOnly = await CreateRide(hostClient, Place("Rampura"), "Female");

        var join = await riderClient.PostAsJsonAsync("/api/riderequests/send", new { ridePostId = postId, requesterId = host.Id });
        Assert.Equal(HttpStatusCode.Created, join.StatusCode);
        Assert.Equal(rider.Id.ToString(), (await JsonOf(join)).GetProperty("data").GetProperty("requesterId").GetString());

        // CreateUserAsync users have no gender set ("Prefer not to say"), so a female-only ride refuses them.
        Assert.Equal(HttpStatusCode.Forbidden, (await riderClient.PostAsJsonAsync("/api/riderequests/send", new { ridePostId = femaleOnly })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await outsiderClient.GetAsync($"/api/riderequests/ride/{postId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsiderClient.GetAsync($"/api/riderequests/user/{rider.Id}")).StatusCode);
        var requests = (await JsonOf(await hostClient.GetAsync($"/api/riderequests/ride/{postId}"))).GetProperty("data").EnumerateArray().ToList();
        var requestId = Assert.Single(requests).GetProperty("_id").GetString();
        Assert.Equal(HttpStatusCode.Forbidden, (await outsiderClient.PutAsync($"/api/riderequests/accept/{requestId}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await hostClient.PutAsync($"/api/riderequests/accept/{requestId}", null)).StatusCode);
    }
}
