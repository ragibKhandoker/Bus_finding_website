using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Tests;

[Collection(ServerCollection.Name)]
public class ChatTests(TestServerFixture server)
{
    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task MakeFriends(User a, User b)
    {
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (user1, user2) = Friendship.Order(a.Id, b.Id);
        db.Friendships.Add(new Friendship { User1Id = user1, User2Id = user2 });
        await db.SaveChangesAsync();
    }

    // Two friends and the id of the conversation between them.
    private async Task<(User A, HttpClient AClient, User B, HttpClient BClient, string ChatId)> FriendsWithChat()
    {
        var (a, aClient) = await server.CreateUserAsync();
        var (b, bClient) = await server.CreateUserAsync();
        await MakeFriends(a, b);
        var open = await aClient.GetAsync($"/api/chat/{b.Id}");
        Assert.Equal(HttpStatusCode.Created, open.StatusCode);
        return (a, aClient, b, bClient, (await JsonOf(open)).GetProperty("chat").GetProperty("_id").GetString()!);
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string chatId, string content) =>
        client.PostAsJsonAsync("/api/chat/send", new { chatId, content });

    private static async Task<List<JsonElement>> Messages(HttpClient client, string chatId, string query = "")
    {
        var response = await client.GetAsync($"/api/chat/messages/{chatId}{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await JsonOf(response)).GetProperty("messages").EnumerateArray().ToList();
    }

    private static async Task<int> UnreadFor(HttpClient client, string chatId)
    {
        var body = await JsonOf(await client.GetAsync("/api/chat/unread-counts"));
        return body.GetProperty("conversations").EnumerateArray()
            .Where(c => c.GetProperty("chatId").GetString() == chatId)
            .Sum(c => c.GetProperty("unreadCount").GetInt32());
    }

    // ---- opening conversations ----

    [SkippableFact]
    public async Task Opening_a_chat_again_reuses_the_conversation()
    {
        RequireServer();
        var (a, aClient, b, bClient, chatId) = await FriendsWithChat();

        var again = await aClient.GetAsync($"/api/chat/{b.Id}");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(chatId, (await JsonOf(again)).GetProperty("chat").GetProperty("_id").GetString());
        var fromOtherSide = await JsonOf(await bClient.GetAsync($"/api/chat/{a.Id}"));
        Assert.Equal(chatId, fromOtherSide.GetProperty("chat").GetProperty("_id").GetString());
        Assert.False(fromOtherSide.GetProperty("chat").GetProperty("otherParticipant").TryGetProperty("email", out _));
    }

    [SkippableFact]
    public async Task Simultaneous_opens_create_one_conversation()
    {
        RequireServer();
        var (a, aClient) = await server.CreateUserAsync();
        var (b, bClient) = await server.CreateUserAsync();
        await MakeFriends(a, b);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            i % 2 == 0 ? aClient.GetAsync($"/api/chat/{b.Id}") : bClient.GetAsync($"/api/chat/{a.Id}")));
        var ids = new HashSet<string>();
        foreach (var response in responses)
        {
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Created });
            ids.Add((await JsonOf(response)).GetProperty("chat").GetProperty("_id").GetString()!);
        }
        Assert.Single(ids);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);

        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Chats.CountAsync(c => (c.User1Id == a.Id && c.User2Id == b.Id) || (c.User1Id == b.Id && c.User2Id == a.Id)));
    }

    [SkippableFact]
    public async Task Chats_need_an_existing_friend()
    {
        RequireServer();
        var (me, client) = await server.CreateUserAsync();
        var (stranger, _) = await server.CreateUserAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/chat/{me.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/chat/{stranger.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/chat/{Guid.NewGuid()}")).StatusCode);
    }

    // ---- sending ----

    [SkippableFact]
    public async Task Sent_message_is_trimmed_saved_and_listed()
    {
        RequireServer();
        var (a, aClient, b, bClient, chatId) = await FriendsWithChat();

        var before = DateTime.UtcNow.AddSeconds(-5);
        var send = await Send(aClient, chatId, "  Hello there!  ");
        Assert.Equal(HttpStatusCode.Created, send.StatusCode);
        var sent = (await JsonOf(send)).GetProperty("message");
        Assert.Equal("Hello there!", sent.GetProperty("content").GetString());
        Assert.Equal(a.Id.ToString(), sent.GetProperty("senderId").GetString());

        await using (var scope = server.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var saved = await db.ChatMessages.AsNoTracking().SingleAsync(m => m.Id == Guid.Parse(sent.GetProperty("_id").GetString()!));
            Assert.Equal(a.Id, saved.SenderId);
            Assert.Equal("Hello there!", saved.Text);
            Assert.False(saved.Read);
            Assert.InRange(saved.CreatedAt, before, DateTime.UtcNow.AddSeconds(5));
        }

        var list = (await JsonOf(await bClient.GetAsync("/api/chat"))).GetProperty("chats").EnumerateArray().ToList();
        var summary = Assert.Single(list, c => c.GetProperty("_id").GetString() == chatId);
        Assert.Equal("Hello there!", summary.GetProperty("lastMessage").GetProperty("text").GetString());
        Assert.Equal(a.Id.ToString(), summary.GetProperty("otherParticipant").GetProperty("id").GetString());
        Assert.Equal(1, summary.GetProperty("unreadCount").GetInt32());
    }

    [SkippableFact]
    public async Task Blank_and_too_long_messages_are_refused()
    {
        RequireServer();
        var (_, aClient, _, _, chatId) = await FriendsWithChat();

        foreach (var blank in new[] { "", "   ", "\n\t  " })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(aClient, chatId, blank)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await aClient.PostAsJsonAsync("/api/chat/send", new { chatId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(aClient, chatId, new string('a', 2001))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Send(aClient, chatId, new string('a', 2000))).StatusCode);
        Assert.Single(await Messages(aClient, chatId));
    }

    [SkippableFact]
    public async Task The_sender_is_always_the_signed_in_user()
    {
        RequireServer();
        var (a, aClient, b, _, chatId) = await FriendsWithChat();

        var response = await aClient.PostAsJsonAsync("/api/chat/send", new { chatId, content = "hi", senderId = b.Id });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(a.Id.ToString(), (await JsonOf(response)).GetProperty("message").GetProperty("senderId").GetString());
    }

    [SkippableFact]
    public async Task Outsiders_cannot_read_send_or_mark_read()
    {
        RequireServer();
        var (_, aClient, _, _, chatId) = await FriendsWithChat();
        var (_, outsider) = await server.CreateUserAsync();
        await Send(aClient, chatId, "private");

        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/chat/messages/{chatId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(outsider, chatId, "let me in")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.PostAsync($"/api/chat/{chatId}/read", null)).StatusCode);
        Assert.Empty((await JsonOf(await outsider.GetAsync("/api/chat"))).GetProperty("chats").EnumerateArray());
        Assert.Single(await Messages(aClient, chatId));
    }

    [SkippableFact]
    public async Task Chat_endpoints_require_a_valid_sign_in()
    {
        RequireServer();
        var (_, aClient, _, _, chatId) = await FriendsWithChat();
        foreach (var client in new[] { server.ClientFor(null), server.ClientFor("not.a.token") })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/chat")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/chat/unread-counts")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/chat/messages/{chatId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Send(client, chatId, "hi")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync($"/api/chat/{chatId}/read", null)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, (await aClient.PostAsync("/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await aClient.GetAsync("/api/chat")).StatusCode);
    }

    // ---- order, paging and read state ----

    [SkippableFact]
    public async Task Messages_come_oldest_first_and_pages_neither_skip_nor_repeat()
    {
        RequireServer();
        var (a, aClient, b, bClient, chatId) = await FriendsWithChat();
        for (var i = 1; i <= 7; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await Send(i % 2 == 0 ? bClient : aClient, chatId, $"m{i}")).StatusCode);
        }

        var latest = await JsonOf(await aClient.GetAsync($"/api/chat/messages/{chatId}?limit=3"));
        Assert.True(latest.GetProperty("hasMore").GetBoolean());
        var pages = new List<List<JsonElement>> { latest.GetProperty("messages").EnumerateArray().ToList() };
        while (true)
        {
            var oldest = pages[0][0].GetProperty("_id").GetString();
            var page = await JsonOf(await aClient.GetAsync($"/api/chat/messages/{chatId}?limit=3&before={oldest}"));
            pages.Insert(0, page.GetProperty("messages").EnumerateArray().ToList());
            if (!page.GetProperty("hasMore").GetBoolean()) break;
        }
        var all = pages.SelectMany(p => p).ToList();
        Assert.Equal(Enumerable.Range(1, 7).Select(i => $"m{i}"), all.Select(m => m.GetProperty("content").GetString()));
        Assert.Equal(7, all.Select(m => m.GetProperty("_id").GetString()).Distinct().Count());
        Assert.Equal(b.Id.ToString(), all[1].GetProperty("senderId").GetString());
        Assert.Equal(a.Id.ToString(), all[0].GetProperty("senderId").GetString());
        var times = all.Select(m => m.GetProperty("createdAt").GetDateTime()).ToList();
        Assert.Equal(times.OrderBy(t => t), times);

        var newer = await Messages(aClient, chatId, $"?after={all[4].GetProperty("_id").GetString()}");
        Assert.Equal(["m6", "m7"], newer.Select(m => m.GetProperty("content").GetString()));
    }

    [SkippableFact]
    public async Task Reading_resets_only_that_conversations_incoming_unread_count()
    {
        RequireServer();
        var (a, aClient, b, bClient, chatAB) = await FriendsWithChat();
        var (c, cClient) = await server.CreateUserAsync();
        await MakeFriends(a, c);
        var chatAC = (await JsonOf(await aClient.GetAsync($"/api/chat/{c.Id}"))).GetProperty("chat").GetProperty("_id").GetString()!;

        await Send(bClient, chatAB, "from b 1");
        await Send(bClient, chatAB, "from b 2");
        await Send(aClient, chatAB, "my own reply");
        await Send(cClient, chatAC, "from c");

        Assert.Equal(2, await UnreadFor(aClient, chatAB));
        Assert.Equal(1, await UnreadFor(aClient, chatAC));
        Assert.Equal(1, await UnreadFor(bClient, chatAB));
        Assert.Equal(3, (await JsonOf(await aClient.GetAsync("/api/chat/unread-counts"))).GetProperty("total").GetInt32());

        // Loading messages alone does not mark anything read.
        await Messages(aClient, chatAB);
        Assert.Equal(2, await UnreadFor(aClient, chatAB));

        var read = await aClient.PostAsync($"/api/chat/{chatAB}/read", null);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(2, (await JsonOf(read)).GetProperty("marked").GetInt32());
        Assert.Equal(0, await UnreadFor(aClient, chatAB));
        Assert.Equal(1, await UnreadFor(aClient, chatAC));
        Assert.Equal(1, await UnreadFor(bClient, chatAB));

        // Reading again is harmless.
        Assert.Equal(0, (await JsonOf(await aClient.PostAsync($"/api/chat/{chatAB}/read", null))).GetProperty("marked").GetInt32());
    }

    [SkippableFact]
    public async Task Read_up_to_a_message_leaves_later_ones_unread()
    {
        RequireServer();
        var (_, aClient, _, bClient, chatId) = await FriendsWithChat();
        var first = (await JsonOf(await Send(bClient, chatId, "seen"))).GetProperty("message").GetProperty("_id").GetString();
        await Send(bClient, chatId, "arrived later");

        Assert.Equal(HttpStatusCode.OK, (await aClient.PostAsync($"/api/chat/{chatId}/read?upTo={first}", null)).StatusCode);
        Assert.Equal(1, await UnreadFor(aClient, chatId));
        Assert.Equal(HttpStatusCode.BadRequest, (await aClient.PostAsync($"/api/chat/{chatId}/read?upTo={Guid.NewGuid()}", null)).StatusCode);
    }
}
