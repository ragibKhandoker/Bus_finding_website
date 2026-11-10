using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Tests;

// F17: the Lost & Found queries, checked without a database (see RideQueryShapeTests).
public class LostFoundQueryShapeTests
{
    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1")
        .UseSnakeCaseNamingConvention().Options);

    private static LostFoundService Service(AppDbContext db) =>
        new(db, null!, null!, Options.Create(new LostFoundOptions()), new NotificationService(db));

    public static TheoryData<string> Queries => new() { "all", "search", "mine", "find", "matches", "chat-list", "chat-messages", "chat-send", "chat-start", "chat-react" };

    private static Task Run(string name, (LostFoundService Service, AppDbContext Db) x) => Run(name, x.Service, x.Db);

    private static Task Run(string name, LostFoundService ls, AppDbContext db) => name switch
    {
        "all" => ls.ListAsync(new LostFoundFilter("lost", "Wallet", null, "open", 2, 12)),
        "search" => ls.ListAsync(new LostFoundFilter(Search: "phone 50%")),
        "mine" => ls.MineAsync(Guid.NewGuid()),
        "find" => ls.FindAsync(Guid.NewGuid()),
        "matches" => ls.MatchesAsync(new LostFound { ReportType = LostFoundTypes.Lost, Category = "Wallet", BusName = "Raida", IncidentDate = new DateOnly(2026, 11, 9) }),
        "chat-list" => new LostFoundChatService(db, new NotificationService(db)).ListAsync(Guid.NewGuid()),
        "chat-messages" => new LostFoundChatService(db, new NotificationService(db)).MessagesAsync(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow),
        "chat-send" => new LostFoundChatService(db, new NotificationService(db)).SendAsync(Guid.NewGuid(), Guid.NewGuid(), "Hello"),
        "chat-start" => new LostFoundChatService(db, new NotificationService(db)).StartAsync(Guid.NewGuid(), Guid.NewGuid(), "Hello"),
        "chat-react" => new LostFoundChatService(db, new NotificationService(db)).ReactAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "👍"),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(Queries))]
    public async Task Query_is_valid_and_only_fails_on_the_missing_database(string name)
    {
        await using var db = Db();
        var error = await Record.ExceptionAsync(() => Run(name, (Service(db), db)));
        Assert.NotNull(error);
        Assert.True(error is not InvalidOperationException || error.Message.Contains("transient failure"), $"{name}: {error.Message} | {error.InnerException?.Message}");
    }
}

// F17 end to end against PostgreSQL (skipped without SMARTCOMMUTE_TEST_DB).
[Collection(ServerCollection.Name)]
public class LostFoundFlowTests(TestServerFixture server)
{
    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    // The smallest real PNG (1×1 pixel).
    private const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<Guid> BusIdAsync()
    {
        await using var scope = server.Scope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Buses.OrderBy(b => b.RouteNumber).Select(b => b.Id).FirstAsync();
    }

    private static string Today => DateOnly.FromDateTime(DhakaTime.FromUtc(DateTime.UtcNow)).ToString("yyyy-MM-dd");

    private object Body(Guid busId, string type = "lost", string item = "Black Samsung phone", string? date = null, string? image = null) => new
    {
        reportType = type, itemName = item, category = "Mobile Phone", busId, location = "Near Farmgate stop", incidentDate = date ?? Today,
        incidentTime = "08:15", description = "Black Samsung phone with a cracked blue cover.", contactInformation = "01712345678", image,
        userId = Guid.NewGuid(), // ignored: the user always comes from the token
    };

    [SkippableFact]
    public async Task Lost_and_found_reports_are_saved_listed_and_owned_by_their_author()
    {
        RequireServer();
        var bus = await BusIdAsync();
        var (owner, ownerClient) = await server.CreateUserAsync();
        var (_, otherClient) = await server.CreateUserAsync();

        var created = await ownerClient.PostAsJsonAsync("/api/lostfound/create", Body(bus, image: Png));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var report = (await JsonOf(created)).GetProperty("data");
        var id = report.GetProperty("id").GetString();
        Assert.Equal("open", report.GetProperty("status").GetString());
        Assert.Equal("lost", report.GetProperty("reportType").GetString());
        Assert.False(string.IsNullOrEmpty(report.GetProperty("busName").GetString()));
        var imageUrl = report.GetProperty("imageUrl").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await otherClient.GetAsync(imageUrl)).StatusCode);

        await using (var scope = server.Scope())
        {
            var saved = await scope.ServiceProvider.GetRequiredService<AppDbContext>().LostFoundReports.AsNoTracking().SingleAsync(r => r.Id == Guid.Parse(id!));
            Assert.Equal(owner.Id, saved.UserId);
        }

        var found = await otherClient.PostAsJsonAsync("/api/lostfound/create", Body(bus, type: "found", item: "Phone found on bus"));
        Assert.Equal(HttpStatusCode.Created, found.StatusCode);

        var mine = await JsonOf(await ownerClient.GetAsync("/api/lostfound/my"));
        Assert.Single(mine.GetProperty("data").EnumerateArray());
        var lost = await JsonOf(await otherClient.GetAsync("/api/lostfound/all?type=lost&search=samsung"));
        Assert.Contains(lost.GetProperty("data").EnumerateArray(), r => r.GetProperty("id").GetString() == id && !r.GetProperty("isMine").GetBoolean());

        // Someone else cannot change or delete it.
        Assert.Equal(HttpStatusCode.Forbidden, (await otherClient.PutAsJsonAsync($"/api/lostfound/{id}", Body(bus, item: "Hacked"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherClient.DeleteAsync($"/api/lostfound/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherClient.PutAsJsonAsync($"/api/lostfound/{id}/status", new { status = "closed" })).StatusCode);

        // The owner can.
        var update = await ownerClient.PutAsJsonAsync($"/api/lostfound/{id}", new
        {
            reportType = "lost", itemName = "Black Samsung A54", category = "Mobile Phone", busId = bus, location = "Farmgate", incidentDate = Today,
            description = "Black Samsung A54, cracked blue cover.", contactInformation = "01712345678", status = "resolved", removeImage = true,
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = (await JsonOf(update)).GetProperty("data");
        Assert.Equal("Black Samsung A54", updated.GetProperty("itemName").GetString());
        Assert.Equal("resolved", updated.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("imageUrl").ValueKind);
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.DeleteAsync($"/api/lostfound/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerClient.GetAsync($"/api/lostfound/{id}")).StatusCode);
    }

    [SkippableFact]
    public async Task Bad_reports_are_refused_with_a_reason()
    {
        RequireServer();
        var bus = await BusIdAsync();
        var (_, client) = await server.CreateUserAsync();
        var tomorrow = DateOnly.FromDateTime(DhakaTime.FromUtc(DateTime.UtcNow)).AddDays(1).ToString("yyyy-MM-dd");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/lostfound/create", Body(bus, date: tomorrow))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/lostfound/create", Body(Guid.NewGuid()))).StatusCode); // not a Dhaka route
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/lostfound/create", Body(bus, type: "stolen"))).StatusCode);
        var fake = "data:image/png;base64," + Convert.ToBase64String("not a picture"u8.ToArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/lostfound/create", Body(bus, image: fake))).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/lostfound/create", Body(bus, item: "Blue wallet"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/lostfound/create", Body(bus, item: "Blue wallet"))).StatusCode); // double submit
    }

    [SkippableFact]
    public async Task Finder_and_owner_message_each_other_privately()
    {
        RequireServer();
        var bus = await BusIdAsync();
        var (owner, ownerClient) = await server.CreateUserAsync();
        var (_, finderClient) = await server.CreateUserAsync();
        var (_, strangerClient) = await server.CreateUserAsync();
        var reportId = (await JsonOf(await ownerClient.PostAsJsonAsync("/api/lostfound/create", Body(bus, item: "Red umbrella")))).GetProperty("data").GetProperty("id").GetString();

        // The owner cannot message their own report.
        Assert.Equal(HttpStatusCode.BadRequest, (await ownerClient.PostAsJsonAsync($"/api/lostfound/{reportId}/conversations", new { text = "Hi" })).StatusCode);
        var start = await finderClient.PostAsJsonAsync($"/api/lostfound/{reportId}/conversations", new { text = "I think I found your umbrella on route 8." });
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        var chatId = (await JsonOf(start)).GetProperty("data").GetProperty("id").GetString();

        var inbox = await JsonOf(await ownerClient.GetAsync("/api/lostfound/conversations"));
        Assert.Equal(1, inbox.GetProperty("unread").GetInt32());
        Assert.Equal(HttpStatusCode.Created, (await ownerClient.PostAsJsonAsync($"/api/lostfound/conversations/{chatId}/messages", new { text = "Yes! Can we meet at Farmgate?" })).StatusCode);
        var thread = await JsonOf(await finderClient.GetAsync($"/api/lostfound/conversations/{chatId}/messages"));
        Assert.Equal(2, thread.GetProperty("data").GetArrayLength());
        Assert.Equal(0, (await JsonOf(await ownerClient.GetAsync("/api/lostfound/conversations"))).GetProperty("unread").GetInt32());

        // Nobody else can read or write in it, and starting again reuses the same conversation.
        Assert.Equal(HttpStatusCode.NotFound, (await strangerClient.GetAsync($"/api/lostfound/conversations/{chatId}/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await strangerClient.PostAsJsonAsync($"/api/lostfound/conversations/{chatId}/messages", new { text = "Hi" })).StatusCode);
        var again = await JsonOf(await finderClient.PostAsJsonAsync($"/api/lostfound/{reportId}/conversations", new { text = "On my way." }));
        Assert.Equal(chatId, again.GetProperty("data").GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await finderClient.PostAsJsonAsync($"/api/lostfound/conversations/{chatId}/messages", new { text = "   " })).StatusCode);
        await using var scope = server.Scope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Notifications.AnyAsync(n => n.UserId == owner.Id && n.Title == "New message about Red umbrella"));
    }

    [SkippableFact]
    public async Task Pictures_and_reactions_work_in_the_chat()
    {
        RequireServer();
        var bus = await BusIdAsync();
        var (_, ownerClient) = await server.CreateUserAsync();
        var (_, finderClient) = await server.CreateUserAsync();
        var (_, strangerClient) = await server.CreateUserAsync();
        var reportId = (await JsonOf(await ownerClient.PostAsJsonAsync("/api/lostfound/create", Body(bus, item: "Green bag")))).GetProperty("data").GetProperty("id").GetString();

        // A screenshot with no words is a valid message; a fake picture is not.
        var start = await finderClient.PostAsJsonAsync($"/api/lostfound/{reportId}/conversations", new { text = "", image = Png });
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        var chatId = (await JsonOf(start)).GetProperty("data").GetProperty("id").GetString();
        var fake = "data:image/png;base64," + Convert.ToBase64String("not a picture"u8.ToArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await finderClient.PostAsJsonAsync($"/api/lostfound/conversations/{chatId}/messages", new { image = fake })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await finderClient.PostAsJsonAsync($"/api/lostfound/conversations/{chatId}/messages", new { text = " " })).StatusCode);

        var thread = await JsonOf(await ownerClient.GetAsync($"/api/lostfound/conversations/{chatId}/messages"));
        var first = thread.GetProperty("data")[0];
        var messageId = first.GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.GetAsync(first.GetProperty("imageUrl").GetString())).StatusCode);

        // The owner reacts, changes the reaction, and the same emoji again takes it away.
        var react = await JsonOf(await ownerClient.PutAsJsonAsync($"/api/lostfound/conversations/{chatId}/messages/{messageId}/reaction", new { emoji = "👍" }));
        Assert.Equal("👍", react.GetProperty("data")[0].GetProperty("emoji").GetString());
        Assert.True(react.GetProperty("data")[0].GetProperty("mine").GetBoolean());
        await finderClient.PutAsJsonAsync($"/api/lostfound/conversations/{chatId}/messages/{messageId}/reaction", new { emoji = "❤️" });
        var changed = await JsonOf(await ownerClient.PutAsJsonAsync($"/api/lostfound/conversations/{chatId}/messages/{messageId}/reaction", new { emoji = "❤️" }));
        Assert.Equal(2, changed.GetProperty("data")[0].GetProperty("count").GetInt32());
        var removed = await JsonOf(await ownerClient.PutAsJsonAsync($"/api/lostfound/conversations/{chatId}/messages/{messageId}/reaction", new { emoji = "❤️" }));
        Assert.Equal(1, removed.GetProperty("data")[0].GetProperty("count").GetInt32());
        Assert.False(removed.GetProperty("data")[0].GetProperty("mine").GetBoolean());

        Assert.Equal(HttpStatusCode.BadRequest, (await ownerClient.PutAsJsonAsync($"/api/lostfound/conversations/{chatId}/messages/{messageId}/reaction", new { emoji = "💩" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await strangerClient.PutAsJsonAsync($"/api/lostfound/conversations/{chatId}/messages/{messageId}/reaction", new { emoji = "👍" })).StatusCode);
        var polled = await JsonOf(await finderClient.GetAsync($"/api/lostfound/conversations/{chatId}/messages?after={Uri.EscapeDataString(DateTime.UtcNow.AddMinutes(1).ToString("O"))}"));
        Assert.Equal(0, polled.GetProperty("data").GetArrayLength());
        Assert.True(polled.GetProperty("reactions").TryGetProperty(messageId!, out _)); // reactions on older messages still come back
    }

    [SkippableFact]
    public async Task Guests_cannot_use_it_and_admins_review_reports()
    {
        RequireServer();
        var bus = await BusIdAsync();
        var guest = server.ClientFor(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/lostfound/all")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PostAsJsonAsync("/api/lostfound/create", Body(bus))).StatusCode);

        var (owner, client) = await server.CreateUserAsync();
        var (_, admin) = await server.CreateAdminAsync();
        var id = (await JsonOf(await client.PostAsJsonAsync("/api/lostfound/create", Body(bus, item: "Office keys")))).GetProperty("data").GetProperty("id").GetString();
        var review = await admin.PutAsJsonAsync($"/api/lostfound/{id}/status", new { status = "under-review", note = "Checking with the bus company." });
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Equal("under-review", (await JsonOf(review)).GetProperty("data").GetProperty("status").GetString());
        // Owners cannot put their own report under review.
        var self = await client.PutAsJsonAsync($"/api/lostfound/{id}", new
        {
            reportType = "lost", itemName = "Office keys", category = "Keys", busId = bus, location = "Mirpur", incidentDate = Today,
            description = "Three keys on a red ring.", contactInformation = "01712345678", status = "under-review",
        });
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        await using var scope = server.Scope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Notifications.AnyAsync(n => n.UserId == owner.Id && n.Title == "Lost & Found report updated"));
    }
}
