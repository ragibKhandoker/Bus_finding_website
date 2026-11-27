using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Tests;

// F20: the suggestion rules and queries, without a database.
public class RouteSuggestionRuleTests
{
    [Theory]
    [InlineData(23.75, 90.39, true)]       // Farmgate area
    [InlineData(22.35, 91.78, false)]      // Chattogram: outside Dhaka City
    [InlineData(95, 90.39, false)]         // not a latitude
    public void Coordinates_must_be_real_and_inside_Dhaka(double lat, double lng, bool ok) =>
        Assert.Equal(ok, RouteSuggestionService.CheckCoordinates(lat, lng).Problem is null);

    [Fact]
    public void Coordinates_come_in_pairs()
    {
        Assert.Null(RouteSuggestionService.CheckCoordinates(null, null).Problem);
        Assert.NotNull(RouteSuggestionService.CheckCoordinates(23.75, null).Problem);
        Assert.NotNull(RouteSuggestionService.CheckCoordinates(double.NaN, 90.39).Problem);
    }

    [Fact]
    public void Text_is_cleaned_of_control_characters()
    {
        Assert.Equal("Bus stops at Farmgate", RouteSuggestionService.Line("  Bus\tstops \u0007at   Farmgate "));
        Assert.Equal("Line one\n\nLine two", RouteSuggestionService.Text("Line one\r\n\r\n\r\n\r\nLine two\u0000"));
    }

    // Stops on a straight north-south line, 0.01° (~1.1 km) apart.
    private static readonly List<(double, double)> Line3 = [(23.70, 90.40), (23.71, 90.40), (23.72, 90.40)];

    [Theory]
    [InlineData(23.705, 90.401, 1)] // between the first two
    [InlineData(23.715, 90.401, 2)] // between the last two
    [InlineData(23.69, 90.40, 0)]   // before the first
    [InlineData(23.73, 90.40, 3)]   // after the last
    public void A_new_stop_goes_where_it_adds_the_least_distance(double lat, double lng, int expected) =>
        Assert.Equal(expected, RouteSuggestionService.BestPosition(Line3, lat, lng));

    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1")
        .UseSnakeCaseNamingConvention().Options);

    public static TheoryData<string> Queries => new() { "mine", "find", "all", "search", "counts", "image" };

    // Every query EF has to translate is valid SQL; it can only fail because there is no database.
    [Theory]
    [MemberData(nameof(Queries))]
    public async Task Query_is_valid_and_only_fails_on_the_missing_database(string name)
    {
        await using var db = Db();
        var service = new RouteSuggestionService(db, null!, null!, Options.Create(new RouteSuggestionOptions()), new NotificationService(db));
        var error = await Record.ExceptionAsync(() => name switch
        {
            "mine" => service.MineAsync(Guid.NewGuid(), 2, 10),
            "find" => service.FindAsync(Guid.NewGuid(), Guid.NewGuid(), false),
            "all" => service.AllAsync(new RouteSuggestionFilter(RouteSuggestionStatus.Pending, RouteSuggestionTypes.NewStop, Page: 2)),
            "search" => service.AllAsync(new RouteSuggestionFilter(Search: "RS-3F2A")),
            "counts" => service.CountsAsync(),
            "image" => service.ImageAsync(Guid.NewGuid(), Guid.NewGuid(), false),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        });
        Assert.NotNull(error);
        Assert.True(error is not InvalidOperationException || error.Message.Contains("transient failure"), $"{name}: {error.Message} | {error.InnerException?.Message}");
    }
}

// F20 end to end against PostgreSQL (skipped without SMARTCOMMUTE_TEST_DB).
[Collection(ServerCollection.Name)]
public class RouteSuggestionFlowTests(TestServerFixture server)
{
    private const string Api = "/api/route-suggestions";
    private const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";

    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    // A Dhaka City route with at least three stops, as the server sees it.
    private async Task<BusViewModel> CityBusAsync()
    {
        await using var scope = server.Scope();
        var routes = scope.ServiceProvider.GetRequiredService<BusRouteCache>();
        routes.Invalidate();
        return (await routes.AllAsync()).First(b => b.IsDhakaCity && b.Stops.Count >= 3 && b.RouteNumber is not null);
    }

    private static async Task<string> CreatedId(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = (await JsonOf(response)).GetProperty("data");
        Assert.Equal("PENDING", data.GetProperty("status").GetString());
        return data.GetProperty("id").GetString()!;
    }

    private async Task<AppDbContext> DbAsync(AsyncServiceScope scope) { await Task.CompletedTask; return scope.ServiceProvider.GetRequiredService<AppDbContext>(); }

    [SkippableFact]
    public async Task A_new_stop_is_added_to_the_route_only_after_approval()
    {
        RequireServer();
        var bus = await CityBusAsync();
        var (owner, user) = await server.CreateUserAsync();
        var (_, admin) = await server.CreateAdminAsync();
        // Half way between the route's first two stops.
        var lat = (bus.Stops[0].Lat + bus.Stops[1].Lat) / 2;
        var lng = (bus.Stops[0].Lng + bus.Stops[1].Lng) / 2;
        var stopName = $"Test Stop {Guid.NewGuid():N}"[..20];
        var id = await CreatedId(await user.PostAsJsonAsync(Api, new
        {
            type = "NEW_STOP", title = "Missing stop near the bridge", description = "The bus always stops here but it is not on the map.",
            // The user only describes the place; the admin enters its coordinates when approving.
            relatedBusId = bus.Id, busStopName = stopName, locationName = "Under the footbridge", image = Png,
            userId = Guid.NewGuid(), status = "APPROVED", // ignored
        }));

        await using (var scope = server.Scope())
        {
            var db = await DbAsync(scope);
            Assert.Equal(owner.Id, (await db.RouteSuggestions.SingleAsync(s => s.Id == Guid.Parse(id))).UserId);
            Assert.False(await db.BusStops.AnyAsync(s => s.Name == stopName)); // nothing official yet
        }
        var mine = await JsonOf(await user.GetAsync($"{Api}/my"));
        Assert.Contains(mine.GetProperty("data").EnumerateArray(), s => s.GetProperty("id").GetString() == id);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync($"{Api}/{id}/image")).StatusCode);

        var preview = (await JsonOf(await admin.GetAsync($"{Api}/{id}/preview"))).GetProperty("data");
        Assert.True(preview.GetProperty("canApprove").GetBoolean());
        Assert.Contains(stopName, preview.GetProperty("changes")[0].GetString());
        Assert.Equal(stopName, preview.GetProperty("needsLocations")[0].GetProperty("name").GetString());

        // Without the coordinates nothing is approved or changed.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PutAsJsonAsync($"{Api}/{id}/review", new { status = "APPROVED" })).StatusCode);
        var approved = await admin.PutAsJsonAsync($"{Api}/{id}/review", new { status = "APPROVED", adminReviewNote = "Checked on the map.", latitude = lat, longitude = lng });
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal("APPROVED", (await JsonOf(approved)).GetProperty("data").GetProperty("status").GetString());

        await using (var scope = server.Scope())
        {
            var db = await DbAsync(scope);
            var stops = await db.BusStops.Where(s => s.BusId == Guid.Parse(bus.Id)).OrderBy(s => s.Order).ToListAsync();
            Assert.Equal(bus.Stops.Count + 1, stops.Count);
            Assert.Equal(stopName, stops[1].Name); // placed between the first two stops
            Assert.Equal(Enumerable.Range(0, stops.Count), stops.Select(s => s.Order)); // orders stay 0..n-1
            Assert.True(await db.Notifications.AnyAsync(n => n.UserId == owner.Id && n.Title.Contains("was approved")));
        }
        // The route list the rest of the site uses shows the new stop at once.
        var route = await JsonOf(await server.ClientFor(null).GetAsync($"/bus/route/{bus.RouteNumber}"));
        Assert.Contains(stopName, route.ToString());

        // A second review of the same suggestion is refused and changes nothing more.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"{Api}/{id}/review", new { status = "APPROVED" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await user.DeleteAsync($"{Api}/{id}")).StatusCode); // no longer pending
    }

    [SkippableFact]
    public async Task A_new_route_and_a_correction_change_the_official_data_when_approved()
    {
        RequireServer();
        var bus = await CityBusAsync();
        var (_, user) = await server.CreateUserAsync();
        var (_, admin) = await server.CreateAdminAsync();
        var number = $"T{Random.Shared.Next(10000, 99999)}";
        var routeId = await CreatedId(await user.PostAsJsonAsync(Api, new
        {
            type = "NEW_ROUTE", title = "New Test Paribahan route", description = "A new company started this route last month.",
            routeName = "Test Paribahan", routeNumber = number, busType = "seating",
            stops = new object[]
            {
                new { name = bus.Stops[0].Name },
                new { name = "Brand New Test Corner" }, // a new place, described only
                new { name = bus.Stops[2].Name },
            },
        }));
        var fixId = await CreatedId(await user.PostAsJsonAsync(Api, new
        {
            type = "INCORRECT_INFORMATION", title = "Stop name is spelled wrong", description = "The name on the sign at the stop is different.",
            relatedBusId = bus.Id, relatedBusStopId = bus.Stops[2].Id, correctionField = "STOP_NAME", correctedValue = bus.Stops[2].Name + " Junction",
        }));

        Assert.Equal(1, (await JsonOf(await admin.GetAsync($"{Api}/{routeId}/preview"))).GetProperty("data").GetProperty("needsLocations")[0].GetProperty("order").GetInt32());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PutAsJsonAsync($"{Api}/{routeId}/review", new { status = "APPROVED" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"{Api}/{routeId}/review", new
        {
            status = "APPROVED", stopLocations = new[] { new { order = 1, lat = bus.Stops[1].Lat + 0.002, lng = bus.Stops[1].Lng } },
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"{Api}/{fixId}/review", new { status = "APPROVED", adminReviewNote = "Matches the sign." })).StatusCode);

        await using var scope = server.Scope();
        var db = await DbAsync(scope);
        var created = await db.Buses.Include(b => b.Stops).SingleAsync(b => b.RouteNumber == number);
        Assert.Equal("Test Paribahan", created.BusName);
        Assert.Equal("seating", created.BusType);
        Assert.Equal(15m, created.BaseFare);
        Assert.Equal(["Brand New Test Corner"], created.Stops.Where(s => s.Order == 1).Select(s => s.Name));
        var renamed = await db.BusStops.SingleAsync(s => s.Id == Guid.Parse(bus.Stops[2].Id!));
        Assert.Equal(bus.Stops[2].Name + " Junction", renamed.Name);
        // The new route is found by the normal bus search.
        var search = await server.ClientFor(null).GetAsync($"/bus/route/{number}");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
    }

    [SkippableFact]
    public async Task Rejections_bad_data_and_other_people_never_change_anything()
    {
        RequireServer();
        var bus = await CityBusAsync();
        var (_, user) = await server.CreateUserAsync();
        var (_, other) = await server.CreateUserAsync();
        var (adminUser, admin) = await server.CreateAdminAsync();
        async Task<HttpStatusCode> Post(HttpClient client, object body) => (await client.PostAsJsonAsync(Api, body)).StatusCode;

        // Bad input is refused before anything is saved.
        Assert.Equal(HttpStatusCode.BadRequest, await Post(user, new { type = "NEW_ROUTE", title = "x", description = "short" }));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(user, new { type = "TELEPORT", title = "Something new", description = "A suggestion of an unknown type." }));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(user, new
        {
            type = "NEW_STOP", title = "Stop in Chattogram", description = "This location is outside Dhaka City.", relatedBusId = bus.Id,
            busStopName = "GEC Circle", latitude = 22.35, longitude = 91.82,
        }));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(user, new
        {
            type = "NEW_STOP", title = "Bad coordinates", description = "Latitude is not a real number here.", relatedBusId = bus.Id,
            busStopName = "Nowhere", latitude = 123.0, longitude = 90.4,
        }));
        Assert.Equal(HttpStatusCode.BadRequest, await Post(user, new
        {
            type = "NEW_STOP", title = "Stop with no place", description = "Neither coordinates nor a description of the place.", relatedBusId = bus.Id,
            busStopName = "Somewhere",
        }));
        Assert.Equal(HttpStatusCode.Conflict, await Post(user, new
        {
            type = "NEW_STOP", title = "Stop already there", description = "This stop is already on the route.", relatedBusId = bus.Id,
            busStopName = bus.Stops[0].Name, latitude = bus.Stops[0].Lat, longitude = bus.Stops[0].Lng,
        }));

        var renameTo = bus.BusName + " Express";
        var id = await CreatedId(await user.PostAsJsonAsync(Api, new
        {
            type = "INCORRECT_INFORMATION", title = "Wrong bus name shown", description = "The company changed its name this year.",
            relatedBusId = bus.Id, correctionField = "BUS_NAME", correctedValue = renameTo,
        }));

        // Other users cannot see or delete it; users cannot use admin endpoints; guests get 401.
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{Api}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"{Api}/{id}")).StatusCode);
        Assert.DoesNotContain((await JsonOf(await other.GetAsync($"{Api}/my"))).GetProperty("data").EnumerateArray(), s => s.GetProperty("id").GetString() == id);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync($"{Api}/all")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PutAsJsonAsync($"{Api}/{id}/review", new { status = "APPROVED" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.ClientFor(null).GetAsync($"{Api}/my")).StatusCode);

        // The admin sees it with the submitter, filters work, and a rejection needs a reason.
        var list = await JsonOf(await admin.GetAsync($"{Api}/all?status=PENDING&type=INCORRECT_INFORMATION"));
        var row = list.GetProperty("data").EnumerateArray().Single(s => s.GetProperty("id").GetString() == id);
        Assert.Equal("Test User", row.GetProperty("user").GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"{Api}/{id}/review", new { status = "REJECTED" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"{Api}/{id}/review", new { status = "MAYBE" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"{Api}/{id}/review", new { status = "REJECTED", adminReviewNote = "The operator still uses the old name." })).StatusCode);
        var seen = (await JsonOf(await user.GetAsync($"{Api}/{id}"))).GetProperty("data");
        Assert.Equal("REJECTED", seen.GetProperty("status").GetString());
        Assert.Equal("The operator still uses the old name.", seen.GetProperty("adminReviewNote").GetString());

        // An admin cannot approve their own suggestion.
        var own = await CreatedId(await admin.PostAsJsonAsync(Api, new
        {
            type = "INCORRECT_INFORMATION", title = "Admin's own correction", description = "Admins cannot approve what they suggest.",
            relatedBusId = bus.Id, correctionField = "BUS_NAME", correctedValue = bus.BusName + " Own",
        }));
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync($"{Api}/{own}/review", new { status = "APPROVED" })).StatusCode);

        // A suggestion that became invalid (its route number taken since) is refused and rolled back as a whole.
        var number = $"T{Random.Shared.Next(10000, 99999)}";
        var stale = await CreatedId(await user.PostAsJsonAsync(Api, new
        {
            type = "INCORRECT_INFORMATION", title = "Route number is wrong", description = "The number painted on the bus is different.",
            relatedBusId = bus.Id, correctionField = "ROUTE_NUMBER", correctedValue = number,
        }));
        await using (var scope = server.Scope())
        {
            var db = await DbAsync(scope);
            db.Buses.Add(new Bus { BusName = "Someone else", RouteNumber = number, Stops = [] });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PutAsJsonAsync($"{Api}/{stale}/review", new { status = "APPROVED" })).StatusCode);

        await using (var scope = server.Scope())
        {
            var db = await DbAsync(scope);
            var official = await db.Buses.AsNoTracking().SingleAsync(b => b.Id == Guid.Parse(bus.Id));
            Assert.Equal(bus.BusName, official.BusName); // neither the rejection nor the refused approvals changed it
            Assert.Equal(bus.RouteNumber, official.RouteNumber);
            Assert.Equal(RouteSuggestionStatus.Pending, (await db.RouteSuggestions.SingleAsync(s => s.Id == Guid.Parse(stale))).Status);
            Assert.Equal(RouteSuggestionStatus.Pending, (await db.RouteSuggestions.SingleAsync(s => s.Id == Guid.Parse(own))).Status);
        }

        // The owner can delete a pending suggestion of their own.
        Assert.Equal(HttpStatusCode.OK, (await user.DeleteAsync($"{Api}/{stale}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"{Api}/{stale}")).StatusCode);
        _ = adminUser;
    }

    [SkippableFact]
    public async Task Owners_edit_their_pending_suggestions_only()
    {
        RequireServer();
        var bus = await CityBusAsync();
        var (_, user) = await server.CreateUserAsync();
        var (_, other) = await server.CreateUserAsync();
        var (_, admin) = await server.CreateAdminAsync();
        object Stop(string title, string where) => new
        {
            type = "NEW_STOP", title, description = "The bus always stops here but it is not shown.", relatedBusId = bus.Id,
            busStopName = "Edited Test Stop", locationName = where, image = Png,
        };
        var id = await CreatedId(await user.PostAsJsonAsync(Api, Stop("Missing stop near the bridge", "Under the footbridge")));

        // The owner changes it (and drops the photo); it is checked again and stays pending.
        var edited = await user.PutAsJsonAsync($"{Api}/{id}", new
        {
            type = "INCORRECT_INFORMATION", title = "Actually the stop name is wrong", description = "I looked again: the name on the sign is different.",
            relatedBusId = bus.Id, relatedBusStopId = bus.Stops[1].Id, correctionField = "STOP_NAME", correctedValue = bus.Stops[1].Name + " Gate", removeImage = true,
        });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var data = (await JsonOf(edited)).GetProperty("data");
        Assert.Equal("INCORRECT_INFORMATION", data.GetProperty("type").GetString());
        Assert.Equal("PENDING", data.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("imageUrl").ValueKind);

        // Bad edits are refused and change nothing; other people cannot edit it.
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PutAsJsonAsync($"{Api}/{id}", new { type = "NEW_STOP", title = "x", description = "short" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"{Api}/{id}", Stop("Taken over by someone else", "Somewhere else"))).StatusCode);
        Assert.Equal("Actually the stop name is wrong", (await JsonOf(await user.GetAsync($"{Api}/{id}"))).GetProperty("data").GetProperty("title").GetString());

        // Once reviewed it can no longer be edited.
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"{Api}/{id}/review", new { status = "REJECTED", adminReviewNote = "Not a real stop name." })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await user.PutAsJsonAsync($"{Api}/{id}", Stop("Trying to change it after review", "Under the footbridge"))).StatusCode);
    }
}
