using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Tests;

// F23: route progress, coordinate checks and freshness, without a database.
public class LiveLocationRuleTests
{
    private static readonly LiveLocationOptions Defaults = new();

    // Mirpur 10 → Farmgate → Shahbagh → Motijheel (real Dhaka coordinates).
    private static List<BusStop> Route() =>
    [
        new() { Order = 0, Name = "Mirpur 10", Lat = 23.8069, Lng = 90.3687 },
        new() { Order = 1, Name = "Farmgate", Lat = 23.7578, Lng = 90.3897 },
        new() { Order = 2, Name = "Shahbagh", Lat = 23.7389, Lng = 90.3958 },
        new() { Order = 3, Name = "Motijheel", Lat = 23.7330, Lng = 90.4172 },
    ];

    [Fact]
    public void Progress_comes_from_the_position_along_the_route()
    {
        var atStart = RouteProgress.Compute(Route(), 23.8069, 90.3687)!;
        Assert.Equal(0, atStart.Percent);
        Assert.Equal("Mirpur 10", atStart.CurrentStop!.Name);
        Assert.Equal("Farmgate", atStart.NextStop!.Name);

        var atFarmgate = RouteProgress.Compute(Route(), 23.7578, 90.3897)!;
        Assert.Equal("Farmgate", atFarmgate.CurrentStop!.Name);
        Assert.Equal("Shahbagh", atFarmgate.NextStop!.Name);
        Assert.Equal(2, atFarmgate.RemainingStops.Count);
        Assert.InRange(atFarmgate.Percent, 40, 75);
        Assert.Equal(atFarmgate.TotalKm, Math.Round(atFarmgate.TravelledKm + atFarmgate.RemainingKm, 2), 2);

        var atEnd = RouteProgress.Compute(Route(), 23.7330, 90.4172)!;
        Assert.Equal(100, atEnd.Percent);
        Assert.Null(atEnd.NextStop);
        Assert.Empty(atEnd.RemainingStops);
    }

    [Fact]
    public void A_bus_between_stops_is_projected_onto_the_route()
    {
        // Halfway between Farmgate and Shahbagh, a little off the straight line.
        var mid = RouteProgress.Compute(Route(), (23.7578 + 23.7389) / 2 + 0.0005, (90.3897 + 90.3958) / 2)!;
        Assert.Null(mid.CurrentStop);
        Assert.Equal("Farmgate", mid.LastPassedStop!.Name);
        Assert.Equal("Shahbagh", mid.NextStop!.Name);
        Assert.True(mid.OnRoute);
        var farmgate = RouteProgress.Compute(Route(), 23.7578, 90.3897)!;
        Assert.True(mid.Percent > farmgate.Percent);
    }

    [Fact]
    public void Return_trips_count_from_the_other_end()
    {
        var bus = new Bus { Stops = Route() };
        var back = RouteProgress.InTravelOrder(bus, DriverTripDirection.Return);
        Assert.Equal("Motijheel", back[0].Name);
        var nearMirpur = RouteProgress.Compute(back, 23.8069, 90.3687)!;
        Assert.Equal(100, nearMirpur.Percent);
    }

    [Fact]
    public void Far_from_the_route_is_reported_as_off_route()
    {
        var result = RouteProgress.Compute(Route(), 23.80, 90.45)!; // Badda side, kilometres away
        Assert.False(result.OnRoute);
        Assert.True(result.OffRouteMeters > 600);
    }

    [Fact]
    public void Routes_without_two_usable_stops_have_no_progress()
    {
        Assert.Null(RouteProgress.Compute([new BusStop { Name = "Only", Lat = 23.8, Lng = 90.4 }], 23.8, 90.4));
        Assert.Null(RouteProgress.Compute([new BusStop { Name = "A" }, new BusStop { Name = "B" }], 23.8, 90.4)); // 0,0 coordinates
    }

    [Theory]
    [InlineData(5, "live")]
    [InlineData(15, "live")]
    [InlineData(16, "delayed")]
    [InlineData(60, "delayed")]
    [InlineData(61, "unavailable")]
    public void Freshness_follows_the_configured_thresholds(int secondsAgo, string expected)
    {
        var now = new DateTime(2026, 11, 12, 6, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, RouteProgress.Freshness(now.AddSeconds(-secondsAgo), now, Defaults));
        Assert.Equal("unavailable", RouteProgress.Freshness(null, now, Defaults));
        Assert.Equal("live", RouteProgress.Freshness(now.AddSeconds(-40), now, new LiveLocationOptions { LiveSeconds = 45, DelayedSeconds = 90 }));
    }

    [Theory]
    [InlineData(23.8103, 90.4125, 15.0, null)]
    [InlineData(null, 90.4125, 15.0, "required")]
    [InlineData(95.0, 90.4125, 15.0, "not valid")]
    [InlineData(22.3569, 91.7832, 15.0, "outside Dhaka")] // Chattogram
    [InlineData(23.8103, 90.4125, 900.0, "precise")]
    public void Coordinates_are_checked(double? lat, double? lng, double accuracy, string? problem)
    {
        var result = LiveLocationService.CoordinateProblem(new LiveLocationUpdate(lat, lng, accuracy, 20, 180), Defaults);
        if (problem is null) Assert.Null(result);
        else Assert.Contains(problem, result);
        Assert.NotNull(LiveLocationService.CoordinateProblem(new LiveLocationUpdate(23.81, 90.41, 10, 400, 0), Defaults)); // speed
        Assert.NotNull(LiveLocationService.CoordinateProblem(new LiveLocationUpdate(23.81, 90.41, 10, 10, 400), Defaults)); // heading
        Assert.NotNull(LiveLocationService.CoordinateProblem(new LiveLocationUpdate(double.NaN, 90.41, 10, 10, 10), Defaults));
    }

    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1")
        .UseSnakeCaseNamingConvention().Options);

    public static TheoryData<string> Queries => new() { "state", "active", "find", "find-session", "info", "stop" };

    [Theory]
    [MemberData(nameof(Queries))]
    public async Task Queries_are_valid_SQL(string name)
    {
        await using var db = Db();
        var service = new LiveLocationService(db, new MemoryCache(new MemoryCacheOptions()), new BusRouteCache(null!), Options.Create(Defaults));
        Task run = name switch
        {
            "state" => service.StateAsync(Guid.NewGuid()),
            "active" => service.ActiveAsync(DateTime.UtcNow),
            "find" => service.FindAsync(Guid.NewGuid(), null),
            "find-session" => service.FindAsync(Guid.NewGuid(), Guid.NewGuid()),
            "info" => db.IndividualBuses.Where(v => v.Id == Guid.NewGuid()).Select(v => new { v.BusId, v.Bus!.BusName }).FirstOrDefaultAsync(),
            _ => service.StopAsync(Guid.NewGuid()),
        };
        var error = await Record.ExceptionAsync(() => run);
        Assert.NotNull(error);
        Assert.True(error is not InvalidOperationException || error.Message.Contains("transient failure"), $"{name}: {error.Message} | {error.InnerException?.Message}");
    }
}

// F23 end to end against PostgreSQL (skipped without SMARTCOMMUTE_TEST_DB).
[Collection(ServerCollection.Name)]
public class LiveLocationFlowTests(TestServerFixture server)
{
    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    // An approved driver with a trip in progress on their assigned bus.
    private async Task<(Guid DriverId, HttpClient Client, Guid BusId, Guid TripId)> DriverOnTripAsync(bool startTrip = true)
    {
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<JwtTokenService>();
        var id = Guid.NewGuid().ToString("N");
        var user = new User { Name = "Live Driver", Email = $"{id}@driver.test", FriendCode = id[..5].ToUpperInvariant(), PasswordHash = "x", Role = UserRoles.Driver };
        db.Users.Add(user);
        db.DriverProfiles.Add(new DriverProfile
        {
            User = user, DateOfBirth = new DateOnly(1990, 1, 1), LicenseNumberHash = id + "l", NidNumberHash = id + "n", LicenseType = "Professional",
            LicenseExpiryDate = new DateOnly(2032, 1, 1), DrivingExperienceYears = 5, EmploymentType = "Full-time",
            VerificationStatus = DriverVerificationStatus.Approved, AccountStatus = DriverAccountStatus.Active,
        });
        var bus = await db.Buses.Include(b => b.Stops).Where(b => b.Stops.Count >= 2).OrderBy(b => b.RouteNumber).FirstAsync();
        db.DriverBusAssignments.Add(new DriverBusAssignment { Driver = user, BusId = bus.Id, IsActive = true });
        var now = DateTime.UtcNow;
        var trip = new DriverTrip
        {
            Id = Guid.NewGuid(), Driver = user, BusId = bus.Id, ScheduleDate = DateOnly.FromDateTime(now), TripNumber = 1, StartStop = "A", EndStop = "B",
            ScheduledDeparture = now.AddMinutes(-5), ScheduledArrival = now.AddMinutes(55),
            Status = startTrip ? DriverTripStatus.InProgress : DriverTripStatus.Scheduled, ActualDeparture = startTrip ? now.AddMinutes(-4) : null,
        };
        db.DriverTrips.Add(trip);
        var session = new UserSession { User = user, CreatedAt = now, ExpiresAt = tokens.NewExpiry() };
        db.UserSessions.Add(session);
        await db.SaveChangesAsync();
        return (user.Id, server.ClientFor(tokens.CreateToken(user, session)), bus.Id, trip.Id);
    }

    private static object Fix(double lat = 23.7578, double lng = 90.3897) => new { latitude = lat, longitude = lng, accuracy = 12, speed = 22, heading = 170 };

    [SkippableFact]
    public async Task Driver_shares_and_commuters_see_the_bus()
    {
        RequireServer();
        var (driverId, driver, busId, tripId) = await DriverOnTripAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await driver.PostAsJsonAsync("/api/live-location/update", Fix())).StatusCode); // not started
        Assert.Equal(HttpStatusCode.OK, (await driver.PostAsync("/api/live-location/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await driver.PostAsJsonAsync("/api/live-location/update", Fix(22.3569, 91.7832))).StatusCode); // Chattogram
        Assert.Equal(HttpStatusCode.OK, (await driver.PostAsJsonAsync("/api/live-location/update", Fix())).StatusCode);

        var (_, commuterA) = await server.CreateUserAsync();
        var (_, commuterB) = await server.CreateUserAsync();
        foreach (var commuter in new[] { commuterA, commuterB }) // several people can follow the same bus
        {
            var bus = (await JsonOf(await commuter.GetAsync($"/api/live-location/bus/{busId}"))).GetProperty("data");
            Assert.True(bus.GetProperty("isSharing").GetBoolean());
            Assert.Equal(23.7578, bus.GetProperty("latitude").GetDouble(), 4);
            Assert.Equal("live", bus.GetProperty("freshness").GetString());
            var text = bus.ToString();
            Assert.DoesNotContain("Live Driver", text);
            Assert.DoesNotContain(driverId.ToString(), text);
        }
        var progress = (await JsonOf(await commuterA.GetAsync($"/api/live-location/bus/{busId}/progress"))).GetProperty("data");
        Assert.True(progress.GetProperty("progress").GetProperty("estimated").GetBoolean());
        Assert.True(progress.GetProperty("route").GetProperty("stops").GetArrayLength() >= 2);

        // The row is updated in place: one per trip.
        await using (var scope = server.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.LiveLocations.CountAsync(l => l.TripId == tripId));
        }

        Assert.Equal(HttpStatusCode.OK, (await driver.PostAsync("/api/live-location/stop", null)).StatusCode);
        var stopped = (await JsonOf(await commuterA.GetAsync($"/api/live-location/bus/{busId}"))).GetProperty("data");
        Assert.False(stopped.GetProperty("isSharing").GetBoolean());
        Assert.Equal(JsonValueKind.Null, stopped.GetProperty("latitude").ValueKind); // no position once sharing stops
        Assert.Equal(HttpStatusCode.Conflict, (await driver.PostAsJsonAsync("/api/live-location/update", Fix())).StatusCode);
    }

    [SkippableFact]
    public async Task Only_a_driver_on_a_trip_can_share_and_only_their_own_bus()
    {
        RequireServer();
        var (_, noTrip, _, _) = await DriverOnTripAsync(startTrip: false);
        Assert.Equal(HttpStatusCode.Conflict, (await noTrip.PostAsync("/api/live-location/start", null)).StatusCode);
        var (_, commuter) = await server.CreateUserAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await commuter.PostAsync("/api/live-location/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await commuter.PostAsJsonAsync("/api/live-location/update", Fix())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.ClientFor(null).GetAsync("/api/live-location/active")).StatusCode);

        // Two drivers: each update only ever moves the sender's own bus, whatever the body says.
        var (_, first, firstBus, _) = await DriverOnTripAsync();
        var (_, second, _, _) = await DriverOnTripAsync();
        await first.PostAsync("/api/live-location/start", null);
        await second.PostAsync("/api/live-location/start", null);
        await first.PostAsJsonAsync("/api/live-location/update", Fix(23.80, 90.37));
        await second.PostAsJsonAsync("/api/live-location/update", new { latitude = 23.74, longitude = 90.40, accuracy = 10, busId = firstBus, driverId = Guid.NewGuid() });
        var mine = (await JsonOf(await first.GetAsync("/api/live-location/me"))).GetProperty("data");
        Assert.Equal(23.80, mine.GetProperty("latitude").GetDouble(), 3);
    }

    [SkippableFact]
    public async Task Completing_the_trip_stops_sharing()
    {
        RequireServer();
        var (driverId, driver, busId, tripId) = await DriverOnTripAsync();
        await driver.PostAsync("/api/live-location/start", null);
        await driver.PostAsJsonAsync("/api/live-location/update", Fix());
        Assert.Equal(HttpStatusCode.OK, (await driver.PostAsync($"/api/driver/trips/{tripId}/complete", null)).StatusCode);
        await using var scope = server.Scope();
        var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().LiveLocations.AsNoTracking().SingleAsync(l => l.TripId == tripId);
        Assert.False(row.IsSharing);
        Assert.Equal("trip-completed", row.StopReason);
        Assert.NotNull(row.Latitude); // kept for the record, not shown
        var (_, commuter) = await server.CreateUserAsync();
        var bus = (await JsonOf(await commuter.GetAsync($"/api/live-location/bus/{busId}"))).GetProperty("data");
        Assert.False(bus.GetProperty("isSharing").GetBoolean());
    }

    [SkippableFact]
    public async Task Old_fixes_are_reported_as_delayed_then_unavailable()
    {
        RequireServer();
        var (driverId, driver, busId, tripId) = await DriverOnTripAsync();
        await driver.PostAsync("/api/live-location/start", null);
        await driver.PostAsJsonAsync("/api/live-location/update", Fix());
        await using (var scope = server.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.LiveLocations.Where(l => l.TripId == tripId).ExecuteUpdateAsync(s => s.SetProperty(l => l.LastUpdatedAt, DateTime.UtcNow.AddSeconds(-40)));
        }
        var (_, commuter) = await server.CreateUserAsync();
        Assert.Equal("delayed", (await JsonOf(await commuter.GetAsync($"/api/live-location/bus/{busId}"))).GetProperty("data").GetProperty("freshness").GetString());
        await using (var scope = server.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.LiveLocations.Where(l => l.TripId == tripId).ExecuteUpdateAsync(s => s.SetProperty(l => l.LastUpdatedAt, DateTime.UtcNow.AddMinutes(-3)));
        }
        var old = (await JsonOf(await commuter.GetAsync($"/api/live-location/bus/{busId}"))).GetProperty("data");
        Assert.Equal("unavailable", old.GetProperty("freshness").GetString());
        Assert.True(old.GetProperty("isSharing").GetBoolean()); // last known position, clearly marked
    }
}
