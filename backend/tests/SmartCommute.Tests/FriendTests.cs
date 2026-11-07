using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Tests;

[Collection(ServerCollection.Name)]
public class FriendTests(TestServerFixture server)
{
    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static Task<HttpResponseMessage> SendRequest(HttpClient client, string friendCode) =>
        client.PostAsJsonAsync("/friends/request", new { friendCode });

    private static async Task<List<JsonElement>> DataOf(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await JsonOf(response)).GetProperty("data").EnumerateArray().ToList();
    }

    private static async Task<List<string>> FriendIds(HttpClient client) =>
        (await DataOf(client, "/friends")).Select(f => f.GetProperty("id").GetString()!).ToList();

    // Sends a request from one user to another and returns its id.
    private static async Task<string> RequestId(HttpClient from, User to)
    {
        var response = await SendRequest(from, to.FriendCode);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonOf(response)).GetProperty("data").GetProperty("_id").GetString()!;
    }

    private async Task<int> FriendshipRows(Guid a, Guid b)
    {
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (user1, user2) = Friendship.Order(a, b);
        return await db.Friendships.CountAsync(f => f.User1Id == user1 && f.User2Id == user2);
    }

    // ---- friend codes ----

    [SkippableFact]
    public async Task Signup_saves_a_friend_code_that_login_does_not_change()
    {
        RequireServer();
        var anonymous = server.ClientFor(null);
        var email = $"{Guid.NewGuid():N}@test.local";
        var signup = await anonymous.PostAsJsonAsync("/auth/signup", new { name = "Code Owner", email, password = "Secret123" });
        Assert.Equal(HttpStatusCode.OK, signup.StatusCode);
        var signupBody = await JsonOf(signup);
        var code = signupBody.GetProperty("user").GetProperty("friendCode").GetString()!;
        Assert.Matches("^[A-Z0-9]{5}$", code);

        await using (var scope = server.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(code, await db.Users.Where(u => u.Email == email).Select(u => u.FriendCode).SingleAsync());
        }

        var login = await anonymous.PostAsJsonAsync("/auth/login", new { email, password = "Secret123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginBody = await JsonOf(login);
        Assert.Equal(code, loginBody.GetProperty("user").GetProperty("friendCode").GetString());

        var mine = await JsonOf(await server.ClientFor(loginBody.GetProperty("token").GetString()).GetAsync("/auth/friend-code"));
        Assert.Equal(code, mine.GetProperty("friendCode").GetString());
    }

    [SkippableFact]
    public async Task Database_rejects_malformed_and_duplicate_friend_codes()
    {
        RequireServer();
        var (existing, _) = await server.CreateUserAsync();
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // After the migrations' backfill every user has a well-formed code.
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
            "SELECT count(*)::int AS \"Value\" FROM users WHERE friend_code !~ '^[A-Z0-9]{5}$'").SingleAsync());

        foreach (var bad in new[] { "abc12", "AB-12", existing.FriendCode })
        {
            db.ChangeTracker.Clear();
            db.Users.Add(new User { Name = "Bad Code", Email = $"{Guid.NewGuid():N}@test.local", FriendCode = bad, PasswordHash = "x" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [SkippableFact]
    public async Task Friendships_must_be_stored_in_canonical_order()
    {
        RequireServer();
        var (a, _) = await server.CreateUserAsync();
        var (b, _) = await server.CreateUserAsync();
        var (user1, user2) = Friendship.Order(a.Id, b.Id);
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Friendships.Add(new Friendship { User1Id = user2, User2Id = user1 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.Friendships.Add(new Friendship { User1Id = user1, User2Id = user2 });
        await db.SaveChangesAsync();
    }

    // ---- search ----

    [SkippableFact]
    public async Task Search_finds_a_user_by_code_and_shows_only_public_details()
    {
        RequireServer();
        var (_, me) = await server.CreateUserAsync();
        var (other, _) = await server.CreateUserAsync();

        var response = await me.GetAsync($"/auth/search-friend?code={other.FriendCode.ToLowerInvariant()}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        var user = body.GetProperty("user");
        Assert.Equal(other.Id.ToString(), user.GetProperty("id").GetString());
        Assert.Equal(other.FriendCode, user.GetProperty("friendCode").GetString());
        Assert.Equal("none", body.GetProperty("relationship").GetString());
        foreach (var hidden in new[] { "email", "phone", "passwordHash", "homeArea", "workArea" })
        {
            Assert.False(user.TryGetProperty(hidden, out _), $"search result exposes {hidden}");
        }
    }

    [SkippableFact]
    public async Task Search_reports_unknown_invalid_and_own_codes()
    {
        RequireServer();
        var (self, me) = await server.CreateUserAsync();

        // Codes from CreateUserAsync are hex, so one containing 'Z' never belongs to anyone.
        Assert.Equal(HttpStatusCode.NotFound, (await me.GetAsync("/auth/search-friend?code=ZZZZZ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await me.GetAsync("/auth/search-friend?code=AB")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await me.GetAsync("/auth/search-friend?code=AB%2D12")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await me.GetAsync("/auth/search-friend")).StatusCode);

        var own = await JsonOf(await me.GetAsync($"/auth/search-friend?code={self.FriendCode}"));
        Assert.Equal("self", own.GetProperty("relationship").GetString());
    }

    // ---- requests ----

    [SkippableFact]
    public async Task Accepted_request_makes_both_users_friends_once()
    {
        RequireServer();
        var (alice, aliceClient) = await server.CreateUserAsync();
        var (bob, bobClient) = await server.CreateUserAsync();
        var (_, carolClient) = await server.CreateUserAsync();

        var requestId = await RequestId(aliceClient, bob);

        var incoming = await DataOf(bobClient, "/friends/requests/incoming");
        var received = Assert.Single(incoming);
        Assert.Equal(requestId, received.GetProperty("_id").GetString());
        Assert.Equal(alice.Id.ToString(), received.GetProperty("sender").GetProperty("id").GetString());
        Assert.Equal("pending", received.GetProperty("status").GetString());
        var sent = Assert.Single(await DataOf(aliceClient, "/friends/requests/sent"));
        Assert.Equal(bob.Id.ToString(), sent.GetProperty("receiver").GetProperty("id").GetString());

        var fromAlice = await JsonOf(await aliceClient.GetAsync($"/auth/search-friend?code={bob.FriendCode}"));
        Assert.Equal("request_sent", fromAlice.GetProperty("relationship").GetString());
        var fromBob = await JsonOf(await bobClient.GetAsync($"/auth/search-friend?code={alice.FriendCode}"));
        Assert.Equal("request_received", fromBob.GetProperty("relationship").GetString());
        Assert.Equal(requestId, fromBob.GetProperty("requestId").GetString());

        // Only the recipient may answer.
        Assert.Equal(HttpStatusCode.Forbidden, (await aliceClient.PostAsync($"/friends/requests/{requestId}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await carolClient.PostAsync($"/friends/requests/{requestId}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await carolClient.PostAsync($"/friends/requests/{requestId}/reject", null)).StatusCode);

        var accept = await bobClient.PostAsync($"/friends/requests/{requestId}/accept", null);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        Assert.Equal("accepted", (await JsonOf(accept)).GetProperty("data").GetProperty("status").GetString());

        Assert.Equal([bob.Id.ToString()], await FriendIds(aliceClient));
        Assert.Equal([alice.Id.ToString()], await FriendIds(bobClient));
        Assert.Equal(1, await FriendshipRows(alice.Id, bob.Id));
        Assert.Empty(await DataOf(bobClient, "/friends/requests/incoming"));
        Assert.Empty(await DataOf(aliceClient, "/friends/requests/sent"));

        // Accepting again, or asking again, changes nothing.
        Assert.Equal(HttpStatusCode.Conflict, (await bobClient.PostAsync($"/friends/requests/{requestId}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SendRequest(aliceClient, bob.FriendCode)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SendRequest(bobClient, alice.FriendCode)).StatusCode);
        Assert.Equal(1, await FriendshipRows(alice.Id, bob.Id));
        var search = await JsonOf(await aliceClient.GetAsync($"/auth/search-friend?code={bob.FriendCode}"));
        Assert.Equal("friends", search.GetProperty("relationship").GetString());
    }

    [SkippableFact]
    public async Task Rejected_request_creates_no_friendship_and_can_be_sent_again()
    {
        RequireServer();
        var (alice, aliceClient) = await server.CreateUserAsync();
        var (bob, bobClient) = await server.CreateUserAsync();

        var requestId = await RequestId(aliceClient, bob);
        Assert.Equal(HttpStatusCode.Forbidden, (await aliceClient.PostAsync($"/friends/requests/{requestId}/reject", null)).StatusCode);
        var reject = await bobClient.PostAsync($"/friends/requests/{requestId}/reject", null);
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        Assert.Equal("rejected", (await JsonOf(reject)).GetProperty("data").GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.Conflict, (await bobClient.PostAsync($"/friends/requests/{requestId}/reject", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await bobClient.PostAsync($"/friends/requests/{requestId}/accept", null)).StatusCode);
        Assert.Equal(0, await FriendshipRows(alice.Id, bob.Id));
        Assert.Empty(await FriendIds(aliceClient));
        Assert.Empty(await DataOf(aliceClient, "/friends/requests/sent"));
        Assert.Empty(await DataOf(bobClient, "/friends/requests/incoming"));

        await RequestId(aliceClient, bob);
    }

    [SkippableFact]
    public async Task Duplicate_and_reverse_requests_are_refused()
    {
        RequireServer();
        var (alice, aliceClient) = await server.CreateUserAsync();
        var (bob, bobClient) = await server.CreateUserAsync();

        await RequestId(aliceClient, bob);
        Assert.Equal(HttpStatusCode.Conflict, (await SendRequest(aliceClient, bob.FriendCode)).StatusCode);
        var reverse = await SendRequest(bobClient, alice.FriendCode.ToLowerInvariant());
        Assert.Equal(HttpStatusCode.Conflict, reverse.StatusCode);
        Assert.Contains("Accept it", (await JsonOf(reverse)).GetProperty("message").GetString());
    }

    [SkippableFact]
    public async Task Simultaneous_requests_in_both_directions_leave_one_pending()
    {
        RequireServer();
        var (alice, aliceClient) = await server.CreateUserAsync();
        var (bob, bobClient) = await server.CreateUserAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            i % 2 == 0 ? SendRequest(aliceClient, bob.FriendCode) : SendRequest(bobClient, alice.FriendCode)));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));

        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.FriendRequests.CountAsync(r => r.Status == FriendRequestStatus.Pending &&
            ((r.FromUserId == alice.Id && r.ToUserId == bob.Id) || (r.FromUserId == bob.Id && r.ToUserId == alice.Id))));
    }

    [SkippableFact]
    public async Task Simultaneous_accepts_create_one_friendship()
    {
        RequireServer();
        var (alice, aliceClient) = await server.CreateUserAsync();
        var (bob, bobClient) = await server.CreateUserAsync();
        var requestId = await RequestId(aliceClient, bob);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => bobClient.PostAsync($"/friends/requests/{requestId}/accept", null)));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        Assert.Equal(1, await FriendshipRows(alice.Id, bob.Id));
    }

    [SkippableFact]
    public async Task Invalid_requests_are_refused()
    {
        RequireServer();
        var (me, client) = await server.CreateUserAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await SendRequest(client, me.FriendCode)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendRequest(client, "AB")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendRequest(client, "AB-12")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/friends/request", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendRequest(client, "ZZZZZ")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/friends/requests/{Guid.NewGuid()}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/friends/requests/{Guid.NewGuid()}/reject", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/friends/requests/not-a-guid/accept", null)).StatusCode);
    }

    [SkippableFact]
    public async Task Friend_endpoints_require_a_valid_sign_in()
    {
        RequireServer();
        var paths = new[] { "/friends", "/friends/requests/incoming", "/friends/requests/sent", "/auth/friend-code", "/auth/search-friend?code=ABCDE" };
        foreach (var client in new[] { server.ClientFor(null), server.ClientFor("not.a.token") })
        {
            foreach (var path in paths)
            {
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
            }
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendRequest(client, "ABCDE")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync($"/friends/requests/{Guid.NewGuid()}/accept", null)).StatusCode);
        }

        // A token whose session was signed out is refused too.
        var (_, signedOut) = await server.CreateUserAsync();
        Assert.Equal(HttpStatusCode.OK, (await signedOut.PostAsync("/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await signedOut.GetAsync("/friends")).StatusCode);
    }

    [SkippableFact]
    public async Task Legacy_endpoints_follow_the_same_rules()
    {
        RequireServer();
        var (alice, aliceClient) = await server.CreateUserAsync();
        var (bob, bobClient) = await server.CreateUserAsync();

        // /api/friends/add no longer connects people directly; it sends a request.
        Assert.Equal(HttpStatusCode.Created, (await aliceClient.PostAsJsonAsync("/api/friends/add", new { friendCode = bob.FriendCode })).StatusCode);
        Assert.Empty((await JsonOf(await aliceClient.GetAsync("/api/friends"))).GetProperty("data").EnumerateArray());

        var pending = (await JsonOf(await bobClient.GetAsync("/auth/pending-friend-requests"))).GetProperty("requests").EnumerateArray().ToList();
        var requestId = Assert.Single(pending).GetProperty("_id").GetString();
        Assert.Equal(HttpStatusCode.OK, (await bobClient.PutAsync($"/auth/accept-friend-request/{requestId}", null)).StatusCode);

        var friends = (await JsonOf(await aliceClient.GetAsync("/auth/friends"))).GetProperty("friends").EnumerateArray().ToList();
        Assert.Equal(bob.Id.ToString(), Assert.Single(friends).GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await aliceClient.PostAsJsonAsync("/auth/send-friend-request", new { toUserId = bob.Id })).StatusCode);

        var found = await JsonOf(await bobClient.GetAsync($"/auth/search-friend/{alice.FriendCode}"));
        Assert.False(found.TryGetProperty("email", out _));
    }
}
