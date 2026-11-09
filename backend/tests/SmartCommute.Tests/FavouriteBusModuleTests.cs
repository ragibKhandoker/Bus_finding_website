using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Tests;

// ---------------- Unit tests: no database needed ----------------

public class DhakaCityAreaTests
{
    private record SeedRoute(string RouteNumber, string Name, List<SeedStop> Stops);
    private record SeedStop(string Name, double Lat, double Lng);

    private static List<SeedRoute> SeedRoutes()
    {
        var assembly = typeof(DhakaCityArea).Assembly;
        using var stream = assembly.GetManifestResourceStream("SmartCommute.Server.Data.dhaka-bus-routes.json")!;
        return JsonSerializer.Deserialize<List<SeedRoute>>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static bool IsCity(SeedRoute route) => DhakaCityArea.Default.IsCityRoute(route.Stops.Select(s => (s.Name, s.Lat, s.Lng)).ToList());

    [Fact]
    public void Seed_data_splits_into_city_and_outside_routes()
    {
        var routes = SeedRoutes();
        Assert.Equal(184, routes.Count);
        Assert.Equal(101, routes.Count(IsCity));
    }

    [Theory]
    [InlineData("DBR-3")]   // runs on to Savar and Hemayetpur
    [InlineData("DBR-5")]   // Gazipur
    [InlineData("DBR-25")]  // Maowa (Munshiganj)
    [InlineData("DBR-115")] // Manikganj and Paturia
    [InlineData("DBR-82")]  // Narayanganj
    public void Routes_that_leave_the_city_are_not_city_routes(string routeNumber) =>
        Assert.False(IsCity(SeedRoutes().Single(r => r.RouteNumber == routeNumber)));

    [Fact]
    public void A_route_with_every_stop_inside_the_city_is_a_city_route()
    {
        var city = SeedRoutes().Where(IsCity).ToList();
        Assert.Contains(city, r => r.Stops.Any(s => s.Name == "Motijheel") || r.Stops.Any(s => s.Name == "Farmgate"));
        Assert.All(city, r => Assert.DoesNotContain(r.Stops, s => s.Name is "Savar" or "Tongi" or "Gazipur" or "Kanchpur"));
    }

    [Fact]
    public void One_outside_stop_or_an_unknown_far_stop_excludes_the_route()
    {
        var area = DhakaCityArea.Default;
        Assert.True(area.IsCityRoute([("Farmgate", 23.7577, 90.3897), ("Motijheel", 23.7330, 90.4172)]));
        // A listed outside stop, even when its coordinates look like the city.
        Assert.False(area.IsCityRoute([("Farmgate", 23.7577, 90.3897), ("  tongi ", 23.80, 90.40)]));
        // An unknown stop far outside the city bounds.
        Assert.False(area.IsCityRoute([("Farmgate", 23.7577, 90.3897), ("Cumilla", 23.46, 91.18)]));
        // A single stop is not a route.
        Assert.False(area.IsCityRoute([("Farmgate", 23.7577, 90.3897)]));
    }
}

public class DhakaTimeTests
{
    [Fact]
    public void A_time_without_an_offset_is_bangladesh_time()
    {
        Assert.Equal(new DateTime(2026, 11, 10, 2, 30, 0, DateTimeKind.Utc), DhakaTime.ParseToUtc("2026-11-10T08:30"));
        Assert.Equal(new DateTime(2026, 11, 9, 18, 15, 0, DateTimeKind.Utc), DhakaTime.ParseToUtc("2026-11-10 00:15"));
    }

    [Fact]
    public void A_time_with_an_offset_is_converted()
    {
        Assert.Equal(new DateTime(2026, 11, 10, 2, 30, 0, DateTimeKind.Utc), DhakaTime.ParseToUtc("2026-11-10T02:30Z"));
        Assert.Equal(new DateTime(2026, 11, 10, 2, 30, 0, DateTimeKind.Utc), DhakaTime.ParseToUtc("2026-11-10T08:30+06:00"));
        Assert.Null(DhakaTime.ParseToUtc("tomorrow morning"));
        Assert.Null(DhakaTime.ParseToUtc(""));
    }

    [Fact]
    public void Formats_in_bangladesh_time()
    {
        var utc = new DateTime(2026, 11, 10, 2, 30, 0, DateTimeKind.Utc);
        Assert.Equal("2026-11-10T08:30+06:00", DhakaTime.Format(utc));
        Assert.Equal("08:30", DhakaTime.Clock(utc));
    }

    [Theory]
    [InlineData("Lal Sabuj (AC)", "AC")]
    [InlineData("BRTC AC", "AC")]
    [InlineData("Shuvo Non-AC", "Non-AC")]
    [InlineData("Achim Paribahan", null)]
    [InlineData("Acme Transport", null)]
    public void Air_conditioning_comes_only_from_what_the_name_says(string name, string? expected) =>
        Assert.Equal(expected, FavouriteInsightsService.AcFromName(name));
}

// ---------------- Integration tests: the real server and a throw-away database ----------------

[Collection(ServerCollection.Name)]
public class FavouriteBusModuleTests(TestServerFixture server)
{
    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    // A new route made for the test (so changing its fare or stops cannot affect other tests), copied from a real
    // seeded route: a Dhaka City one, or one that runs outside the city.
    private async Task<Bus> NewBusAsync(bool city)
    {
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeded = await db.Buses.Include(b => b.Stops).AsNoTracking().Where(b => !b.RouteNumber!.StartsWith("T-")).ToListAsync();
        var source = seeded.First(b => DhakaCityArea.Default.IsCityRoute(b) == city);
        var bus = new Bus
        {
            BusName = $"Test {source.BusName}", RouteNumber = $"T-{Guid.NewGuid().ToString("N")[..10]}", Operator = source.Operator,
            BusType = source.BusType, BaseFare = 10, PerKmFare = 2.45m,
            Stops = source.Stops.OrderBy(s => s.Order).Select(s => new BusStop { Order = s.Order, Name = s.Name, Lat = s.Lat, Lng = s.Lng }).ToList(),
            IndividualBuses = [new IndividualBus { BusCode = $"T{Guid.NewGuid().ToString("N")[..12]}", TotalPassengerCapacity = 2, BusType = source.BusType }],
        };
        db.Buses.Add(bus);
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<BusRouteCache>().Invalidate();
        return bus;
    }

    private async Task<FavouriteMonitorReport> RunMonitorAsync(DateTime? now = null)
    {
        await using var scope = server.Scope();
        return await scope.ServiceProvider.GetRequiredService<FavouriteMonitor>().RunOnceAsync(now ?? DateTime.UtcNow);
    }

    private async Task<List<Notification>> NotificationsOf(Guid userId, string category)
    {
        await using var scope = server.Scope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Notifications
            .Where(n => n.UserId == userId && n.Category == category).AsNoTracking().ToListAsync();
    }

    private static Task<HttpResponseMessage> Favourite(HttpClient client, Guid busId) =>
        client.PostAsJsonAsync("/api/favourites", new { busId });

    [SkippableFact]
    public async Task Favourite_add_remove_is_idempotent_and_survives_a_new_request()
    {
        RequireServer();
        var bus = await NewBusAsync(city: true);
        var (user, client) = await server.CreateUserAsync();

        Assert.Equal(HttpStatusCode.Created, (await Favourite(client, bus.Id)).StatusCode);
        // A duplicate is not a second row.
        var again = await Favourite(client, bus.Id);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.False((await JsonOf(again)).GetProperty("created").GetBoolean());

        // Kept on the server: a fresh request (a page reload) sees it.
        Assert.True((await JsonOf(await client.GetAsync($"/api/favourites/check/{bus.Id}"))).GetProperty("isFavourite").GetBoolean());
        var mine = await JsonOf(await client.GetAsync("/api/favourites/my"));
        Assert.Equal(1, mine.GetProperty("count").GetInt32());
        Assert.True(mine.GetProperty("data")[0].GetProperty("isDhakaCity").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/favourites/{bus.Id}")).StatusCode);
        Assert.False((await JsonOf(await client.GetAsync($"/api/favourites/check/{bus.Id}"))).GetProperty("isFavourite").GetBoolean());

        // Both were real events, so both were notified; nothing else was.
        var notes = await NotificationsOf(user.Id, NotificationCategories.BusFavourite);
        Assert.Equal(["Favourite added", "Favourite removed"], notes.OrderBy(n => n.CreatedAt).Select(n => n.Title));
    }

    [SkippableFact]
    public async Task Outside_dhaka_buses_are_refused_by_every_entry_point()
    {
        RequireServer();
        var outside = await NewBusAsync(city: false);
        var (me, client) = await server.CreateUserAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Favourite(client, outside.Id)).StatusCode);
        // The older address shares the rule.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/favbus/add", new { busId = outside.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/favourites/recently-viewed", new { busId = outside.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.GetAsync($"/api/favourites/{outside.Id}/price-history")).StatusCode);

        // A favourite saved before the rule (written straight to the table) still cannot enter the other tools.
        await using (var scope = server.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.FavoriteBuses.Add(new FavoriteBus { UserId = me.Id, BusId = outside.Id, BusName = outside.BusName });
            await db.SaveChangesAsync();
        }
        var city = await NewBusAsync(city: true);
        await Favourite(client, city.Id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/favourites/compare", new { busIds = new[] { city.Id, outside.Id } })).StatusCode);
        var collection = await JsonOf(await client.PostAsJsonAsync("/api/favourite-collections", new { name = "Daily" }));
        var collectionId = collection.GetProperty("data").GetProperty("_id").GetString();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync($"/api/favourite-collections/{collectionId}/buses", new { busId = outside.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/favourites/reminders",
            new { busId = outside.Id, stopName = outside.Stops[0].Name, departureAt = DhakaTime.Format(DateTime.UtcNow.AddHours(3)), leadMinutes = 15 })).StatusCode);
    }

    [SkippableFact]
    public async Task Users_cannot_reach_each_others_favourites_or_collections()
    {
        RequireServer();
        var bus = await NewBusAsync(city: true);
        var (owner, ownerClient) = await server.CreateUserAsync();
        var (_, otherClient) = await server.CreateUserAsync();
        await Favourite(ownerClient, bus.Id);
        var created = await JsonOf(await ownerClient.PostAsJsonAsync("/api/favourite-collections", new { name = "Office" }));
        var id = created.GetProperty("data").GetProperty("_id").GetString();

        Assert.Equal(HttpStatusCode.Forbidden, (await otherClient.GetAsync($"/api/favbus/user/{owner.Id}")).StatusCode);
        Assert.Equal(0, (await JsonOf(await otherClient.GetAsync("/api/favourites/my"))).GetProperty("count").GetInt32());
        Assert.Equal(0, (await JsonOf(await otherClient.GetAsync("/api/favourite-collections"))).GetProperty("count").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.PutAsJsonAsync($"/api/favourite-collections/{id}", new { name = "Mine now" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.DeleteAsync($"/api/favourite-collections/{id}")).StatusCode);
        // Removing "their" favourite only touches the caller's own list.
        await otherClient.DeleteAsync($"/api/favourites/{bus.Id}");
        Assert.True((await JsonOf(await ownerClient.GetAsync($"/api/favourites/check/{bus.Id}"))).GetProperty("isFavourite").GetBoolean());
        // Without a token, nothing.
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.ClientFor(null).GetAsync("/api/favourites/my")).StatusCode);
    }

    [SkippableFact]
    public async Task Collections_hold_only_the_users_favourites_and_moves_are_atomic()
    {
        RequireServer();
        var bus = await NewBusAsync(city: true);
        var notFavourite = await NewBusAsync(city: true);
        var (_, client) = await server.CreateUserAsync();
        await Favourite(client, bus.Id);

        var office = (await JsonOf(await client.PostAsJsonAsync("/api/favourite-collections", new { name = "Office route" }))).GetProperty("data").GetProperty("_id").GetString();
        var uni = (await JsonOf(await client.PostAsJsonAsync("/api/favourite-collections", new { name = "University" }))).GetProperty("data").GetProperty("_id").GetString();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/favourite-collections", new { name = "  office   ROUTE " })).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/favourite-collections/{office}/buses", new { busId = notFavourite.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/favourite-collections/{office}/buses", new { busId = bus.Id })).StatusCode);
        var moved = await JsonOf(await client.PostAsJsonAsync($"/api/favourite-collections/{uni}/buses", new { busId = bus.Id, fromCollectionId = office }));
        Assert.Equal(1, moved.GetProperty("data").GetProperty("busCount").GetInt32());

        var list = (await JsonOf(await client.GetAsync("/api/favourite-collections"))).GetProperty("data").EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("busCount").GetInt32());
        Assert.Equal(0, list["Office route"]);
        Assert.Equal(1, list["University"]);

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/favourite-collections/{uni}", new { name = "Uni buses" })).StatusCode);
        // Removing the favourite takes it out of its collections.
        await client.DeleteAsync($"/api/favourites/{bus.Id}");
        var after = (await JsonOf(await client.GetAsync("/api/favourite-collections"))).GetProperty("data").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "Uni buses");
        Assert.Equal(0, after.GetProperty("busCount").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/favourite-collections/{uni}")).StatusCode);
    }

    [SkippableFact]
    public async Task Comparison_uses_the_database_figures_and_highlights_the_lowest_fare()
    {
        RequireServer();
        var a = await NewBusAsync(city: true);
        var b = await NewBusAsync(city: true);
        await using (var scope = server.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Buses.Where(x => x.Id == b.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.BaseFare, 8m));
            scope.ServiceProvider.GetRequiredService<BusRouteCache>().Invalidate();
        }
        var (_, client) = await server.CreateUserAsync();
        await Favourite(client, a.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/favourites/compare", new { busIds = new[] { a.Id, b.Id } })).StatusCode);
        await Favourite(client, b.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/favourites/compare", new { busIds = new[] { a.Id } })).StatusCode);

        var result = (await JsonOf(await client.PostAsJsonAsync("/api/favourites/compare", new { busIds = new[] { a.Id, b.Id } }))).GetProperty("data");
        var rows = result.GetProperty("buses").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(a.Stops.Count, rows[0].GetProperty("totalStops").GetInt32());
        Assert.Equal(a.Stops.OrderBy(s => s.Order).First().Name, rows[0].GetProperty("startingPoint").GetString());
        Assert.Equal(b.Id.ToString(), result.GetProperty("highlights").GetProperty("lowestFareBusId").GetString());
        Assert.Equal(2, rows[0].GetProperty("seatsFreeNow").GetInt32());
    }

    [SkippableFact]
    public async Task Fare_and_route_changes_are_tracked_and_notified_only_when_they_happen()
    {
        RequireServer();
        var bus = await NewBusAsync(city: true);
        var (user, client) = await server.CreateUserAsync();
        await Favourite(client, bus.Id);

        // Nothing changed: no fare or route notifications.
        await RunMonitorAsync();
        Assert.Empty(await NotificationsOf(user.Id, NotificationCategories.BusFare));
        Assert.Empty(await NotificationsOf(user.Id, NotificationCategories.BusRoute));

        await using (var scope = server.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Buses.Where(x => x.Id == bus.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.BaseFare, 7m));
            var last = await db.BusStops.Where(s => s.BusId == bus.Id).OrderByDescending(s => s.Order).FirstAsync();
            db.BusStops.Remove(last);
            await db.SaveChangesAsync();
        }
        var report = await RunMonitorAsync();
        Assert.True(report.FareChanges >= 1 && report.RouteChanges >= 1);

        var fare = Assert.Single(await NotificationsOf(user.Id, NotificationCategories.BusFare));
        Assert.Equal("Dhaka bus fare decreased", fare.Title);
        Assert.Contains("৳10 to ৳7 (৳3 less)", fare.Message);
        Assert.Contains("removed stop", Assert.Single(await NotificationsOf(user.Id, NotificationCategories.BusRoute)).Message);

        var tracking = (await JsonOf(await client.GetAsync("/api/favourites/price-tracking"))).GetProperty("data").EnumerateArray()
            .Single(x => x.GetProperty("busId").GetString() == bus.Id.ToString());
        Assert.Equal(10m, tracking.GetProperty("previous_base_fare").GetDecimal());
        Assert.Equal(7m, tracking.GetProperty("current_base_fare").GetDecimal());
        Assert.Equal("decreased", tracking.GetProperty("direction").GetString());
        var history = (await JsonOf(await client.GetAsync($"/api/favourites/{bus.Id}/price-history"))).GetProperty("data");
        Assert.Equal(["decreased", "baseline"], history.EnumerateArray().Select(h => h.GetProperty("change").GetString()));

        // Checking again without a change adds nothing.
        await RunMonitorAsync();
        Assert.Single(await NotificationsOf(user.Id, NotificationCategories.BusFare));
    }

    [SkippableFact]
    public async Task Reminders_use_bangladesh_time_and_are_sent_once_when_due()
    {
        RequireServer();
        var bus = await NewBusAsync(city: true);
        var (user, client) = await server.CreateUserAsync();
        await Favourite(client, bus.Id);
        var stop = bus.Stops.OrderBy(s => s.Order).First().Name;
        var departureUtc = DateTime.UtcNow.AddHours(5);
        departureUtc = new DateTime(departureUtc.Year, departureUtc.Month, departureUtc.Day, departureUtc.Hour, departureUtc.Minute, 0, DateTimeKind.Utc);
        var dhakaText = DhakaTime.FromUtc(departureUtc).ToString("yyyy-MM-dd'T'HH:mm");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/favourites/reminders", new { busId = bus.Id, stopName = "Nowhere", departureAt = dhakaText, leadMinutes = 15 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/favourites/reminders", new { busId = bus.Id, stopName = stop, departureAt = dhakaText, leadMinutes = 20 })).StatusCode);
        var created = await client.PostAsJsonAsync("/api/favourites/reminders", new { busId = bus.Id, stopName = stop, departureAt = dhakaText, leadMinutes = 30 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var data = (await JsonOf(created)).GetProperty("data");
        // Typed as Dhaka time, stored as the matching UTC instant.
        Assert.Equal(departureUtc, data.GetProperty("departureAt").GetDateTime().ToUniversalTime());
        Assert.Equal(dhakaText + "+06:00", data.GetProperty("departureAtLocal").GetString());

        await RunMonitorAsync(departureUtc.AddMinutes(-31));
        Assert.Empty(await NotificationsOf(user.Id, NotificationCategories.BusReminder));
        await RunMonitorAsync(departureUtc.AddMinutes(-29));
        var sent = Assert.Single(await NotificationsOf(user.Id, NotificationCategories.BusReminder));
        Assert.Contains($"at {DhakaTime.Clock(departureUtc)} (Bangladesh time)", sent.Message);
        await RunMonitorAsync(departureUtc.AddMinutes(-10));
        Assert.Single(await NotificationsOf(user.Id, NotificationCategories.BusReminder));
    }

    [SkippableFact]
    public async Task Availability_alerts_report_real_seat_changes_only()
    {
        RequireServer();
        var bus = await NewBusAsync(city: true);
        var (user, client) = await server.CreateUserAsync();
        await Favourite(client, bus.Id);
        var stops = bus.Stops.OrderBy(s => s.Order).ToList();
        var created = await client.PostAsJsonAsync("/api/favourites/availability-alerts", new { busId = bus.Id, fromStop = stops[0].Name, toStop = stops[^1].Name });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/favourites/availability-alerts", new { busId = bus.Id, fromStop = stops[0].Name, toStop = stops[^1].Name })).StatusCode);

        await RunMonitorAsync();
        Assert.Empty(await NotificationsOf(user.Id, NotificationCategories.BusAvailability));

        // The only vehicle fills up (as riders boarding would do), then frees a seat.
        async Task SetPassengers(int count)
        {
            await using var scope = server.Scope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().IndividualBuses.Where(v => v.BusId == bus.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.CurrentPassengerCount, count));
        }
        await SetPassengers(2);
        await RunMonitorAsync();
        Assert.Contains("full", Assert.Single(await NotificationsOf(user.Id, NotificationCategories.BusAvailability)).Message);
        await SetPassengers(1);
        await RunMonitorAsync();
        Assert.Equal(2, (await NotificationsOf(user.Id, NotificationCategories.BusAvailability)).Count);
    }

    [SkippableFact]
    public async Task Recently_viewed_keeps_ten_without_duplicates_and_can_be_cleared()
    {
        RequireServer();
        var (_, client) = await server.CreateUserAsync();
        var buses = new List<Bus>();
        for (var i = 0; i < 11; i++) buses.Add(await NewBusAsync(city: true));
        foreach (var bus in buses) await client.PostAsJsonAsync("/api/favourites/recently-viewed", new { busId = bus.Id });
        await client.PostAsJsonAsync("/api/favourites/recently-viewed", new { busId = buses[5].Id });

        var list = (await JsonOf(await client.GetAsync("/api/favourites/recently-viewed"))).GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(10, list.Count);
        Assert.Equal(buses[5].Id.ToString(), list[0].GetProperty("busId").GetString());
        Assert.DoesNotContain(list, x => x.GetProperty("busId").GetString() == buses[0].Id.ToString());
        Assert.Equal(list.Count, list.Select(x => x.GetProperty("busId").GetString()).Distinct().Count());

        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync("/api/favourites/recently-viewed")).StatusCode);
        Assert.Empty((await JsonOf(await client.GetAsync("/api/favourites/recently-viewed"))).GetProperty("data").EnumerateArray());
    }

    [SkippableFact]
    public async Task Notification_center_lists_marks_and_deletes_only_module_notifications()
    {
        RequireServer();
        var bus = await NewBusAsync(city: true);
        var (user, client) = await server.CreateUserAsync();
        await Favourite(client, bus.Id);
        await using (var scope = server.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Notifications.Add(new Notification { UserId = user.Id, Title = "Friend", Message = "x", Category = NotificationCategories.Friend });
            await db.SaveChangesAsync();
        }
        var page = await JsonOf(await client.GetAsync("/api/favourites/notifications"));
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        Assert.Equal(1, page.GetProperty("unreadCount").GetInt32());
        var id = page.GetProperty("data")[0].GetProperty("_id").GetString();

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsync($"/api/favourites/notifications/{id}/read", null)).StatusCode);
        Assert.Equal(0, (await JsonOf(await client.GetAsync("/api/favourites/notifications"))).GetProperty("unreadCount").GetInt32());
        // Another user cannot touch it.
        var (_, other) = await server.CreateUserAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/favourites/notifications/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/favourites/notifications/{id}")).StatusCode);
        // The friend notification is outside the module and untouched.
        Assert.Single(await NotificationsOf(user.Id, NotificationCategories.Friend));
    }

    [SkippableFact]
    public async Task Existing_bus_search_still_works_and_marks_city_buses()
    {
        RequireServer();
        var (_, client) = await server.CreateUserAsync();
        var all = (await JsonOf(await client.GetAsync("/bus/all"))).GetProperty("data").EnumerateArray().ToList();
        Assert.True(all.Count >= 184);
        Assert.Contains(all, b => b.GetProperty("isDhakaCity").GetBoolean());
        Assert.Contains(all, b => !b.GetProperty("isDhakaCity").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/bus/search-by-route?startLocation=Farmgate&endLocation=Motijheel")).StatusCode);
    }
}
