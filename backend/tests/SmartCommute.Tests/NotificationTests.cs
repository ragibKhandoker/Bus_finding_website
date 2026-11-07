using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Tests;

public class SosLinkTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("bn-BD")]
    [InlineData("de-DE")]
    public void Map_link_uses_plain_digits_whatever_the_server_language(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            Assert.Equal("https://www.google.com/maps?q=23.810332,90.412518", NotificationService.MapLink(23.8103321, 90.4125181));
            Assert.Equal("https://www.google.com/maps?q=-33.8,151", NotificationService.MapLink(-33.8, 151));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}

[Collection(ServerCollection.Name)]
public class NotificationTests(TestServerFixture server)
{
    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<Guid> Seed(Guid userId, string title, string category = NotificationCategories.General, bool read = false)
    {
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var n = new Notification { UserId = userId, Title = title, Message = title, Category = category, IsRead = read };
        db.Notifications.Add(n);
        await db.SaveChangesAsync();
        return n.Id;
    }

    private async Task<List<Notification>> Of(Guid userId)
    {
        await using var scope = server.Scope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Notifications.Where(n => n.UserId == userId).AsNoTracking().ToListAsync();
    }

    private static async Task<int> Unread(HttpClient client, Guid userId) =>
        (await JsonOf(await client.GetAsync($"/api/notifications/unread/{userId}"))).GetProperty("count").GetInt32();

    private async Task MakeFriends(User a, User b)
    {
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (u1, u2) = Friendship.Order(a.Id, b.Id);
        db.Friendships.Add(new Friendship { User1Id = u1, User2Id = u2 });
        await db.SaveChangesAsync();
    }

    // ---- reading and managing ----

    [SkippableFact]
    public async Task Unread_count_follows_read_delete_and_clear()
    {
        RequireServer();
        var (me, client) = await server.CreateUserAsync();
        var first = await Seed(me.Id, "One");
        var second = await Seed(me.Id, "Two");
        await Seed(me.Id, "Three");
        await Seed(me.Id, "Old", read: true);

        Assert.Equal(3, await Unread(client, me.Id));
        var list = await JsonOf(await client.GetAsync($"/api/notifications/user/{me.Id}"));
        Assert.Equal(4, list.GetProperty("total").GetInt32());
        Assert.Equal(3, list.GetProperty("unreadCount").GetInt32());
        var times = list.GetProperty("data").EnumerateArray().Select(n => n.GetProperty("createdAt").GetDateTime()).ToList();
        Assert.Equal(times.OrderByDescending(t => t), times);

        var read = await client.PatchAsync($"/api/notifications/read/{first}", null);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.True((await JsonOf(read)).GetProperty("data").GetProperty("isRead").GetBoolean());
        Assert.Equal(2, (await JsonOf(read)).GetProperty("unreadCount").GetInt32());

        var delete = await client.DeleteAsync($"/api/notifications/{second}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.Equal(1, (await JsonOf(delete)).GetProperty("unreadCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/notifications/{second}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsync($"/api/notifications/read-all/{me.Id}", null)).StatusCode);
        Assert.Equal(0, await Unread(client, me.Id));

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/notifications/clear/{me.Id}")).StatusCode);
        Assert.Empty(await Of(me.Id));
    }

    [SkippableFact]
    public async Task Nobody_can_read_or_change_someone_elses_notifications()
    {
        RequireServer();
        var (owner, ownerClient) = await server.CreateUserAsync();
        var (other, otherClient) = await server.CreateUserAsync();
        var mine = await Seed(owner.Id, "Private");
        await Seed(other.Id, "Theirs");

        Assert.Equal(HttpStatusCode.Forbidden, (await otherClient.GetAsync($"/api/notifications/user/{owner.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherClient.GetAsync($"/api/notifications/unread/{owner.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.PatchAsync($"/api/notifications/read/{mine}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.DeleteAsync($"/api/notifications/{mine}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherClient.PatchAsync($"/api/notifications/read-all/{owner.Id}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherClient.DeleteAsync($"/api/notifications/clear/{owner.Id}")).StatusCode);
        Assert.False((await Of(owner.Id)).Single().IsRead);

        // Clearing your own list leaves everyone else's alone.
        Assert.Equal(HttpStatusCode.OK, (await otherClient.DeleteAsync($"/api/notifications/clear/{other.Id}")).StatusCode);
        Assert.Single(await Of(owner.Id));

        foreach (var guest in new[] { server.ClientFor(null), server.ClientFor("not.a.token") })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync($"/api/notifications/user/{owner.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PostAsJsonAsync("/api/notifications/sos", new { latitude = 23.8, longitude = 90.4 })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.GetAsync($"/api/notifications/user/{owner.Id}")).StatusCode);
    }

    // ---- triggers ----

    [SkippableFact]
    public async Task Friend_and_ride_workflows_notify_the_right_people()
    {
        RequireServer();
        var (alice, aliceClient) = await server.CreateUserAsync();
        var (bob, bobClient) = await server.CreateUserAsync();

        Assert.Equal(HttpStatusCode.Created, (await aliceClient.PostAsJsonAsync("/friends/request", new { friendCode = bob.FriendCode })).StatusCode);
        var friendNote = Assert.Single(await Of(bob.Id), n => n.Category == NotificationCategories.Friend);
        Assert.Equal(alice.Id, friendNote.SenderId);

        var ride = await aliceClient.PostAsJsonAsync("/api/rideshare", new { source = "Mirpur", destination = "Banani", maxParticipants = 3, genderPreference = "Any" });
        var rideId = (await JsonOf(ride)).GetProperty("data").GetProperty("_id").GetString();
        var join = await bobClient.PostAsJsonAsync("/api/riderequests", new { ridePostId = rideId });
        var requestId = (await JsonOf(join)).GetProperty("data").GetProperty("_id").GetString();
        var hostNote = Assert.Single(await Of(alice.Id), n => n.Category == NotificationCategories.Ride);
        Assert.Equal(bob.Id, hostNote.SenderId);
        Assert.Equal(Guid.Parse(rideId!), hostNote.RelatedId);

        Assert.Equal(HttpStatusCode.OK, (await aliceClient.PutAsync($"/api/riderequests/accept/{requestId}", null)).StatusCode);
        Assert.Single(await Of(bob.Id), n => n.Category == NotificationCategories.Ride && n.Title == "Ride request accepted");
        Assert.DoesNotContain(await Of(alice.Id), n => n.Title == "Ride request accepted");

        // Completing the ride pays gems, and each rider hears about it.
        Assert.Equal(HttpStatusCode.OK, (await aliceClient.PostAsJsonAsync("/api/rideshare/complete", new { postId = rideId })).StatusCode);
        Assert.Single(await Of(alice.Id), n => n.Category == NotificationCategories.Reward);
        Assert.Single(await Of(bob.Id), n => n.Category == NotificationCategories.Reward);
    }

    // ---- SOS ----

    [SkippableFact]
    public async Task Sos_reaches_only_friends_and_ride_companions_with_a_map_link()
    {
        RequireServer();
        var (sender, senderClient) = await server.CreateUserAsync();
        var (friend, _) = await server.CreateUserAsync();
        var (rider, riderClient) = await server.CreateUserAsync();
        var (stranger, _) = await server.CreateUserAsync();
        await MakeFriends(sender, friend);
        var ride = await senderClient.PostAsJsonAsync("/api/rideshare", new { source = "Mirpur", destination = "Uttara", maxParticipants = 3, genderPreference = "Any" });
        var rideId = (await JsonOf(ride)).GetProperty("data").GetProperty("_id").GetString();
        var join = await riderClient.PostAsJsonAsync("/api/riderequests", new { ridePostId = rideId });
        await senderClient.PutAsync($"/api/riderequests/accept/{(await JsonOf(join)).GetProperty("data").GetProperty("_id").GetString()}", null);

        var sos = await senderClient.PostAsJsonAsync("/api/notifications/sos", new { latitude = 23.8103321, longitude = 90.4125181, serviceName = "Medical", userId = stranger.Id });
        Assert.Equal(HttpStatusCode.Created, sos.StatusCode);
        Assert.Equal(2, (await JsonOf(sos)).GetProperty("count").GetInt32());

        foreach (var recipient in new[] { friend.Id, rider.Id })
        {
            var alert = Assert.Single(await Of(recipient), n => n.Category == NotificationCategories.Sos);
            Assert.Equal("https://www.google.com/maps?q=23.810332,90.412518", alert.ActionUrl);
            Assert.Equal(sender.Id, alert.SenderId);
            Assert.Contains(sender.Name, alert.Title);
            Assert.Contains("medical help", alert.Message);
        }
        Assert.DoesNotContain(await Of(stranger.Id), n => n.Category == NotificationCategories.Sos);
        Assert.DoesNotContain(await Of(sender.Id), n => n.Category == NotificationCategories.Sos);
    }

    [SkippableFact]
    public async Task Sos_needs_a_real_location_and_someone_to_tell()
    {
        RequireServer();
        var (sender, client) = await server.CreateUserAsync();

        foreach (var body in new object[] { new { }, new { latitude = 23.8 }, new { latitude = 91, longitude = 90 }, new { latitude = 23.8, longitude = 181 } })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/notifications/sos", body)).StatusCode);
        }
        var nobody = await client.PostAsJsonAsync("/api/notifications/sos", new { latitude = 23.8, longitude = 90.4 });
        Assert.Equal(HttpStatusCode.Conflict, nobody.StatusCode);
        Assert.Contains("No alert was sent", (await JsonOf(nobody)).GetProperty("message").GetString());
    }
}
