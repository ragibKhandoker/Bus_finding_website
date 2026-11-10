using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Tests;

// F19: freshness, duplicate slots and the queries, without a database.
public class BusCrowdReportRuleTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 6, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, "VERY_RECENT")]
    [InlineData(15, "VERY_RECENT")]
    [InlineData(16, "RECENT")]
    [InlineData(30, "RECENT")]
    [InlineData(31, "AGING")]
    [InlineData(60, "AGING")]
    [InlineData(61, "EXPIRED")]
    [InlineData(600, "EXPIRED")]
    public void Freshness_comes_from_the_observation_age(int minutesAgo, string expected)
    {
        Assert.Equal(expected, CrowdFreshness.Of(Now.AddMinutes(-minutesAgo), Now));
    }

    [Fact]
    public void Expired_reports_are_not_current_and_cannot_be_edited_late()
    {
        var report = new BusCrowdReport
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), BusName = "Raida", ObservationDateTime = Now.AddMinutes(-90),
            CreatedAt = Now.AddMinutes(-80), UpdatedAt = Now.AddMinutes(-80), CrowdLevel = CrowdLevels.Low, SeatAvailability = SeatAvailabilities.Available,
        };
        var forOwner = CrowdReportViewModel.From(report, report.UserId, Now);
        Assert.Equal("EXPIRED", forOwner.Freshness);
        Assert.False(forOwner.IsCurrent);
        Assert.Equal(90, forOwner.AgeMinutes);
        Assert.True(forOwner.IsMine);
        Assert.False(forOwner.CanEdit);

        var fresh = CrowdReportViewModel.From(new BusCrowdReport { UserId = report.UserId, ObservationDateTime = Now.AddMinutes(-2), CreatedAt = Now.AddMinutes(-1) }, report.UserId, Now);
        Assert.True(fresh.CanEdit);
        Assert.True(fresh.IsCurrent);
        var forOthers = CrowdReportViewModel.From(report, Guid.NewGuid(), Now);
        Assert.False(forOthers.IsMine);
        Assert.Null(forOthers.EditableUntil);
        Assert.False(CrowdReportViewModel.From(report, Guid.Empty, Now).IsMine); // anonymous viewers
    }

    [Fact]
    public void Times_in_the_same_ten_minutes_share_a_slot()
    {
        var a = new DateTime(2026, 11, 10, 2, 0, 0, DateTimeKind.Utc);
        Assert.Equal(BusCrowdReport.SlotOf(a), BusCrowdReport.SlotOf(a.AddMinutes(9)));
        Assert.NotEqual(BusCrowdReport.SlotOf(a), BusCrowdReport.SlotOf(a.AddMinutes(10)));
    }

    [Theory]
    [InlineData("low", "LOW")]
    [InlineData(" High ", "HIGH")]
    [InlineData("extreme", null)]
    public void Crowd_levels_are_normalised(string text, string? expected) => Assert.Equal(expected, BusCrowdReportService.NormalizeCrowd(text));

    [Theory]
    [InlineData("available", "AVAILABLE")]
    [InlineData("No Seats", "NO_SEATS")]
    [InlineData("NoSeats", "NO_SEATS")]
    [InlineData("no-seats", "NO_SEATS")]
    [InlineData("plenty", null)]
    public void Seat_availability_is_normalised(string text, string? expected) => Assert.Equal(expected, BusCrowdReportService.NormalizeSeats(text));

    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1")
        .UseSnakeCaseNamingConvention().Options);

    public static TheoryData<string> Queries => new() { "find", "mine", "mine-page-2", "live", "recent", "least", "most", "seats", "filters", "very-recent", "recent-band", "aging", "expired", "all" };

    private static Task Run(string name, BusCrowdReportService s) => name switch
    {
        "find" => s.FindAsync(Guid.NewGuid()),
        "mine" => s.MineAsync(Guid.NewGuid(), 1, 20),
        "mine-page-2" => s.MineAsync(Guid.NewGuid(), 2, 20),
        "live" => s.LiveAsync(Now),
        "recent" => s.SearchAsync(new CrowdReportFilter(), Now),
        "least" => s.SearchAsync(new CrowdReportFilter(Sort: CrowdReportFilter.SortLeastCrowded), Now),
        "most" => s.SearchAsync(new CrowdReportFilter(Sort: CrowdReportFilter.SortMostCrowded), Now),
        "seats" => s.SearchAsync(new CrowdReportFilter(Sort: CrowdReportFilter.SortSeats, Page: 2), Now),
        "filters" => s.SearchAsync(new CrowdReportFilter("Raida", Guid.NewGuid(), "Farm_gate%", CrowdLevels.Low, SeatAvailabilities.NoSeats, new DateOnly(2026, 11, 10)), Now),
        "very-recent" => s.SearchAsync(new CrowdReportFilter(Freshness: CrowdFreshness.VeryRecent), Now),
        "recent-band" => s.SearchAsync(new CrowdReportFilter(Freshness: CrowdFreshness.Recent), Now),
        "aging" => s.SearchAsync(new CrowdReportFilter(Freshness: CrowdFreshness.Aging), Now),
        "expired" => s.SearchAsync(new CrowdReportFilter(Freshness: CrowdFreshness.Expired), Now),
        "all" => s.SearchAsync(new CrowdReportFilter(Freshness: CrowdReportFilter.All), Now),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    // EF Core must be able to translate every query; the only failure allowed is the missing database.
    [Theory]
    [MemberData(nameof(Queries))]
    public async Task Query_is_valid_and_only_fails_on_the_missing_database(string name)
    {
        await using var db = Db();
        var service = new BusCrowdReportService(db, null!);
        var error = await Record.ExceptionAsync(() => Run(name, service));
        Assert.NotNull(error);
        Assert.True(error is not InvalidOperationException || error.Message.Contains("transient failure"), $"{name}: {error.Message} | {error.InnerException?.Message}");
    }
}

// F19 end to end against PostgreSQL (skipped without SMARTCOMMUTE_TEST_DB).
[Collection(ServerCollection.Name)]
public class BusCrowdReportFlowTests(TestServerFixture server)
{
    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    // A seeded Dhaka route and two of its stops.
    private async Task<(Guid Bus, Guid Stop, Guid OtherStop, Guid OtherRouteStop)> RouteAsync()
    {
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var buses = await db.Buses.Include(b => b.Stops).OrderBy(b => b.RouteNumber).Take(2).AsNoTracking().ToListAsync();
        var stops = buses[0].Stops.OrderBy(s => s.Order).ToList();
        return (buses[0].Id, stops[0].Id, stops[1].Id, buses[1].Stops.First().Id);
    }

    private static (string Date, string Time) Dhaka(DateTime utc)
    {
        var local = DhakaTime.FromUtc(utc);
        return (local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), local.ToString("HH:mm", CultureInfo.InvariantCulture));
    }

    private static object Body(Guid bus, Guid stop, DateTime? observedUtc = null, string crowd = "MEDIUM", string seats = "LIMITED", int? seatCount = 3,
        string direction = "FORWARD")
    {
        var (date, time) = Dhaka(observedUtc ?? DateTime.UtcNow.AddMinutes(-2));
        return new
        {
            busId = bus, busStopId = stop, travelDirection = direction, crowdLevel = crowd, seatAvailability = seats, observationDate = date, observationTime = time,
            availableSeatCount = seatCount, standingPassengerCount = 6, additionalComments = "  Ladies seats   full ", userId = Guid.NewGuid(),
        };
    }

    [SkippableFact]
    public async Task Commuters_submit_view_edit_and_delete_their_own_reports()
    {
        RequireServer();
        var route = await RouteAsync();
        var (owner, ownerClient) = await server.CreateUserAsync();
        var (_, otherClient) = await server.CreateUserAsync();
        var anonymous = server.ClientFor(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/bus-crowd-reports", Body(route.Bus, route.Stop))).StatusCode);

        var created = await ownerClient.PostAsJsonAsync("/api/bus-crowd-reports", Body(route.Bus, route.Stop));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var data = (await JsonOf(created)).GetProperty("data");
        var id = data.GetProperty("id").GetString()!;
        Assert.Equal("VERY_RECENT", data.GetProperty("freshness").GetString());
        Assert.True(data.GetProperty("canEdit").GetBoolean());
        Assert.Equal("Ladies seats full", data.GetProperty("additionalComments").GetString());
        Assert.False(data.TryGetProperty("userId", out _));

        await using (var scope = server.Scope())
        {
            var saved = await scope.ServiceProvider.GetRequiredService<AppDbContext>().BusCrowdReports.AsNoTracking().SingleAsync(r => r.Id == Guid.Parse(id));
            Assert.Equal(owner.Id, saved.UserId); // never the userId in the body
            Assert.Equal(route.Stop, saved.BusStopId);
        }

        // Everyone sees it, without who made it; only the owner sees it as theirs.
        var publicList = (await JsonOf(await anonymous.GetAsync($"/api/bus-crowd-reports/bus/{route.Bus}"))).GetProperty("data");
        var seen = Assert.Single(publicList.EnumerateArray(), r => r.GetProperty("id").GetString() == id);
        Assert.False(seen.GetProperty("isMine").GetBoolean());
        Assert.True((await JsonOf(await ownerClient.GetAsync($"/api/bus-crowd-reports/{id}"))).GetProperty("data").GetProperty("isMine").GetBoolean());
        var live = (await JsonOf(await anonymous.GetAsync("/api/bus-crowd-reports/live"))).GetProperty("data");
        Assert.Contains(live.EnumerateArray(), b => b.GetProperty("busId").GetString() == route.Bus.ToString());

        // History is private to each user.
        Assert.Single((await JsonOf(await ownerClient.GetAsync("/api/bus-crowd-reports/my"))).GetProperty("data").EnumerateArray());
        Assert.Empty((await JsonOf(await otherClient.GetAsync("/api/bus-crowd-reports/my"))).GetProperty("data").EnumerateArray());
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/bus-crowd-reports/my")).StatusCode);

        // Nobody else can change or delete it.
        Assert.Equal(HttpStatusCode.Forbidden, (await otherClient.PutAsJsonAsync($"/api/bus-crowd-reports/{id}", Body(route.Bus, route.Stop, crowd: "LOW"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherClient.DeleteAsync($"/api/bus-crowd-reports/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/bus-crowd-reports/{id}")).StatusCode);

        var updated = await ownerClient.PutAsJsonAsync($"/api/bus-crowd-reports/{id}", Body(route.Bus, route.Stop, crowd: "HIGH", seats: "NO_SEATS", seatCount: null));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("HIGH", (await JsonOf(updated)).GetProperty("data").GetProperty("crowdLevel").GetString());

        // After the edit window it is locked.
        await using (var scope = server.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var report = await db.BusCrowdReports.SingleAsync(r => r.Id == Guid.Parse(id));
            report.CreatedAt = DateTime.UtcNow.AddMinutes(-20);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await ownerClient.PutAsJsonAsync($"/api/bus-crowd-reports/{id}", Body(route.Bus, route.Stop, crowd: "LOW"))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await ownerClient.DeleteAsync($"/api/bus-crowd-reports/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/bus-crowd-reports/{id}")).StatusCode);
    }

    [SkippableFact]
    public async Task Bad_and_duplicate_reports_are_refused()
    {
        RequireServer();
        var route = await RouteAsync();
        var (_, client) = await server.CreateUserAsync();
        var (_, otherClient) = await server.CreateUserAsync();
        var url = "/api/bus-crowd-reports";

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(url, Body(Guid.NewGuid(), route.Stop))).StatusCode); // unknown bus
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(url, Body(route.Bus, Guid.NewGuid()))).StatusCode); // unknown stop
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(url, Body(route.Bus, route.OtherRouteStop))).StatusCode); // stop of another route
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(url, Body(route.Bus, route.Stop, DateTime.UtcNow.AddHours(2)))).StatusCode); // future
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(url, Body(route.Bus, route.Stop, seats: "NO_SEATS", seatCount: 4))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(url, Body(route.Bus, route.Stop, seatCount: -1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(url, Body(route.Bus, route.Stop, crowd: "EXTREME"))).StatusCode);

        var seenAt = DateTime.UtcNow.AddMinutes(-3);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(url, Body(route.Bus, route.Stop, seenAt))).StatusCode);
        // The same sighting again (a double click, or a few minutes apart) is refused…
        var again = await client.PostAsJsonAsync(url, Body(route.Bus, route.Stop, seenAt.AddMinutes(2), crowd: "LOW"));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("already reported", (await JsonOf(again)).GetProperty("message").GetString());
        // …but another commuter, or another stop, is a separate report.
        Assert.Equal(HttpStatusCode.Created, (await otherClient.PostAsJsonAsync(url, Body(route.Bus, route.Stop, seenAt))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(url, Body(route.Bus, route.OtherStop, seenAt))).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{url}?crowdLevel=EXTREME")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{url}?sort=random")).StatusCode);
    }

    [SkippableFact]
    public async Task Expired_reports_are_kept_in_history_but_never_shown_as_current()
    {
        RequireServer();
        var route = await RouteAsync();
        var (_, client) = await server.CreateUserAsync();
        var oldTime = DateTime.UtcNow.AddMinutes(-90);
        var created = await client.PostAsJsonAsync("/api/bus-crowd-reports", Body(route.Bus, route.OtherStop, oldTime, crowd: "LOW", seats: "AVAILABLE", seatCount: 12));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var data = (await JsonOf(created)).GetProperty("data");
        var id = data.GetProperty("id").GetString();
        Assert.Equal("EXPIRED", data.GetProperty("freshness").GetString());
        Assert.False(data.GetProperty("isCurrent").GetBoolean());

        var current = (await JsonOf(await client.GetAsync($"/api/bus-crowd-reports?busId={route.Bus}"))).GetProperty("data");
        Assert.DoesNotContain(current.EnumerateArray(), r => r.GetProperty("id").GetString() == id);
        var live = (await JsonOf(await client.GetAsync("/api/bus-crowd-reports/live"))).GetProperty("data");
        Assert.DoesNotContain(live.EnumerateArray(), b => b.GetProperty("latest").GetProperty("id").GetString() == id);
        var expired = (await JsonOf(await client.GetAsync($"/api/bus-crowd-reports?busId={route.Bus}&freshness=EXPIRED"))).GetProperty("data");
        Assert.Contains(expired.EnumerateArray(), r => r.GetProperty("id").GetString() == id);
        Assert.Contains((await JsonOf(await client.GetAsync("/api/bus-crowd-reports/my"))).GetProperty("data").EnumerateArray(), r => r.GetProperty("id").GetString() == id);
    }
}
