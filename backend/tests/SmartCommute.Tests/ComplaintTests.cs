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

// F18: the complaint status rules and queries, without a database.
public class ComplaintRuleTests
{
    [Theory]
    [InlineData("PENDING", "REVIEWING", true)]
    [InlineData("PENDING", "REJECTED", true)]
    [InlineData("REVIEWING", "RESOLVED", true)]
    [InlineData("REVIEWING", "REJECTED", true)]
    [InlineData("RESOLVED", "REVIEWING", true)]
    [InlineData("PENDING", "RESOLVED", false)]
    [InlineData("PENDING", "PENDING", false)]
    [InlineData("RESOLVED", "REJECTED", false)]
    [InlineData("REJECTED", "RESOLVED", false)]
    public void Only_the_allowed_status_changes_are_possible(string from, string to, bool allowed)
    {
        Assert.Equal(allowed, ComplaintStatus.CanMove(from, to));
    }

    [Fact]
    public void Complaint_codes_are_short_and_upper_case()
    {
        var id = Guid.Parse("3f2a9c1b-0000-0000-0000-000000000000");
        Assert.Equal("CMP-3F2A9C1B", SmartCommute.Server.ViewModels.ComplaintViewModel.CodeOf(id));
    }

    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1")
        .UseSnakeCaseNamingConvention().Options);

    public static TheoryData<string> Queries => new() { "mine", "find", "all", "search", "history", "stats", "evidence" };

    private static Task Run(string name, ComplaintService s) => name switch
    {
        "mine" => s.MineAsync(Guid.NewGuid()),
        "find" => s.FindAsync(Guid.NewGuid(), Guid.NewGuid(), false),
        "all" => s.AllAsync(new ComplaintFilter(Category: "Harassment", Status: "PENDING", From: new DateOnly(2026, 1, 1), To: new DateOnly(2026, 12, 1), Oldest: true, Page: 2)),
        "search" => s.AllAsync(new ComplaintFilter(Search: "CMP-3F2A")),
        "history" => s.HistoryAsync(Guid.NewGuid(), Guid.NewGuid(), true),
        "stats" => s.StatsAsync(DateTime.UtcNow),
        "evidence" => s.EvidenceAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), false),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(Queries))]
    public async Task Query_is_valid_and_only_fails_on_the_missing_database(string name)
    {
        await using var db = Db();
        var service = new ComplaintService(db, null!, null!, Options.Create(new ComplaintOptions()), new NotificationService(db));
        var error = await Record.ExceptionAsync(() => Run(name, service));
        Assert.NotNull(error);
        Assert.True(error is not InvalidOperationException || error.Message.Contains("transient failure"), $"{name}: {error.Message} | {error.InnerException?.Message}");
    }
}

// F18 end to end against PostgreSQL (skipped without SMARTCOMMUTE_TEST_DB).
[Collection(ServerCollection.Name)]
public class ComplaintFlowTests(TestServerFixture server)
{
    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    private const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<Guid> BusIdAsync()
    {
        await using var scope = server.Scope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Buses.OrderBy(b => b.RouteNumber).Select(b => b.Id).FirstAsync();
    }

    private static string Today => DateOnly.FromDateTime(DhakaTime.FromUtc(DateTime.UtcNow)).ToString("yyyy-MM-dd");

    private static object Body(Guid busId, string subject = "Conductor took extra fare", string? date = null, string[]? evidence = null, string? stop = null) => new
    {
        busId, category = "Overcharging / Extra Fare", subject, description = "The conductor asked for 40 taka for a 20 taka trip and shouted at me.",
        incidentDate = date ?? Today, incidentLocation = "Farmgate", stopName = stop, evidence, userId = Guid.NewGuid(), status = "RESOLVED",
    };

    [SkippableFact]
    public async Task Users_submit_track_and_delete_their_own_complaints()
    {
        RequireServer();
        var bus = await BusIdAsync();
        var (owner, ownerClient) = await server.CreateUserAsync();
        var (_, otherClient) = await server.CreateUserAsync();

        var created = await ownerClient.PostAsJsonAsync("/api/complaints/create", Body(bus, evidence: [Png]));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var data = (await JsonOf(created)).GetProperty("data");
        var id = data.GetProperty("id").GetString();
        Assert.Equal("PENDING", data.GetProperty("status").GetString()); // the client's "status" is ignored
        Assert.StartsWith("CMP-", data.GetProperty("code").GetString());
        var evidenceUrl = data.GetProperty("evidence")[0].GetProperty("url").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.GetAsync(evidenceUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync(evidenceUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.ClientFor(null).GetAsync(evidenceUrl)).StatusCode);

        await using (var scope = server.Scope())
        {
            var saved = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Complaints.AsNoTracking().SingleAsync(c => c.Id == Guid.Parse(id!));
            Assert.Equal(owner.Id, saved.UserId);
        }
        Assert.Single((await JsonOf(await ownerClient.GetAsync("/api/complaints/my"))).GetProperty("data").EnumerateArray());
        Assert.Empty((await JsonOf(await otherClient.GetAsync("/api/complaints/my"))).GetProperty("data").EnumerateArray());

        // Nobody else can see, delete or review it; the owner cannot change its status.
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync($"/api/complaints/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync($"/api/complaints/{id}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.DeleteAsync($"/api/complaints/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ownerClient.PutAsJsonAsync($"/api/complaints/{id}/status", new { status = "RESOLVED", adminNotes = "I fixed it myself." })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ownerClient.GetAsync("/api/complaints/all")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ownerClient.GetAsync("/api/complaints/stats")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await ownerClient.DeleteAsync($"/api/complaints/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerClient.GetAsync($"/api/complaints/{id}")).StatusCode);
    }

    [SkippableFact]
    public async Task Bad_complaints_are_refused()
    {
        RequireServer();
        var bus = await BusIdAsync();
        var (_, client) = await server.CreateUserAsync();
        var tomorrow = DateOnly.FromDateTime(DhakaTime.FromUtc(DateTime.UtcNow)).AddDays(1).ToString("yyyy-MM-dd");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/complaints/create", Body(bus, date: tomorrow))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/complaints/create", Body(Guid.NewGuid()))).StatusCode); // not a Dhaka route
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/complaints/create", Body(bus, stop: "Chittagong"))).StatusCode); // not on the route
        var fake = "data:image/png;base64," + Convert.ToBase64String("not a picture"u8.ToArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/complaints/create", Body(bus, evidence: [fake]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/complaints/create", new { busId = bus, category = "Weather", subject = "Rain", description = "short", incidentDate = Today, incidentLocation = "X" })).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/complaints/create", Body(bus, subject: "Bus skipped Shahbag stop"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/complaints/create", Body(bus, subject: "Bus skipped Shahbag stop"))).StatusCode);
    }

    [SkippableFact]
    public async Task Admins_review_complaints_with_notes_and_history()
    {
        RequireServer();
        var bus = await BusIdAsync();
        var (owner, ownerClient) = await server.CreateUserAsync();
        var (_, admin) = await server.CreateAdminAsync();
        var id = (await JsonOf(await ownerClient.PostAsJsonAsync("/api/complaints/create", Body(bus, subject: "Driver was rude to passengers")))).GetProperty("data").GetProperty("id").GetString();

        var list = await JsonOf(await admin.GetAsync($"/api/complaints/all?search=CMP-{id![..8]}&status=PENDING"));
        Assert.Contains(list.GetProperty("data").EnumerateArray(), c => c.GetProperty("id").GetString() == id);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/complaints/{id}/status", new { status = "REVIEWING", adminNotes = "" })).StatusCode); // a note is required
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/complaints/{id}/status", new { status = "RESOLVED", adminNotes = "Skipping the review." })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/complaints/{id}/status", new { status = "REVIEWING", adminNotes = "We are checking with the bus company." })).StatusCode);
        // Once it is being reviewed, the owner can no longer delete it.
        Assert.Equal(HttpStatusCode.Conflict, (await ownerClient.DeleteAsync($"/api/complaints/{id}")).StatusCode);
        var resolved = await admin.PutAsJsonAsync($"/api/complaints/{id}/status", new { status = "RESOLVED", adminNotes = "Complaint reviewed. Necessary action has been taken." });
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);

        var seen = (await JsonOf(await ownerClient.GetAsync($"/api/complaints/{id}"))).GetProperty("data");
        Assert.Equal("RESOLVED", seen.GetProperty("status").GetString());
        Assert.Equal("Complaint reviewed. Necessary action has been taken.", seen.GetProperty("adminNotes").GetString());
        var history = (await JsonOf(await ownerClient.GetAsync($"/api/complaints/{id}/history"))).GetProperty("data");
        Assert.Equal(["PENDING", "REVIEWING", "RESOLVED"], history.EnumerateArray().Select(h => h.GetProperty("toStatus").GetString()).ToArray());

        var stats = (await JsonOf(await admin.GetAsync("/api/complaints/stats"))).GetProperty("data");
        Assert.True(stats.GetProperty("resolved").GetInt32() >= 1);
        await using var scope = server.Scope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Notifications.AnyAsync(n => n.UserId == owner.Id && n.Title.Contains("was resolved")));
    }
}
