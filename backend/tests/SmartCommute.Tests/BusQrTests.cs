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

// QR boarding: reading the token out of what a camera saw, and the nearest-stop helpers, without a database.
public class BusQrRuleTests
{
    private static readonly string Token = BusQrService.NewSecret();

    [Fact]
    public void Tokens_are_random_url_safe_and_43_characters()
    {
        Assert.Equal(43, Token.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", Token);
        Assert.NotEqual(Token, BusQrService.NewSecret());
    }

    [Fact]
    public void The_token_is_read_from_the_link_or_on_its_own()
    {
        Assert.Equal(Token, BusQrService.TokenFrom(Token));
        Assert.Equal(Token, BusQrService.TokenFrom($"  https://smartcommute-dhaka.vercel.app/scan?c={Token}  "));
        Assert.Equal(Token, BusQrService.TokenFrom($"http://localhost:5173/scan?x=1&c={Token}"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("https://example.com/scan")]
    [InlineData("https://example.com/scan?c=short")]
    [InlineData("ftp://example.com/scan?c=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'; DROP TABLE trips;--")]
    public void Anything_else_is_not_a_bus_code(string scanned) => Assert.Null(BusQrService.TokenFrom(scanned));

    [Fact]
    public void Only_the_hash_identifies_a_token()
    {
        Assert.Equal(64, BusQrService.Hash(Token).Length);
        Assert.Equal(BusQrService.Hash(Token), BusQrService.Hash(Token));
        Assert.NotEqual(BusQrService.Hash(Token), BusQrService.Hash(BusQrService.NewSecret()));
    }

    private static readonly BusViewModel Route = new()
    {
        BusName = "Test Paribahan",
        Stops = [new() { Name = "A", Lat = 23.70, Lng = 90.40 }, new() { Name = "B", Lat = 23.71, Lng = 90.40 }, new() { Name = "C", Lat = 23.72, Lng = 90.40 }],
    };

    [Fact]
    public void The_nearest_stop_is_found_among_the_allowed_ones()
    {
        Assert.Equal(1, TripService.NearestStop(Route, 23.7102, 90.4001)!.Value.Index);
        Assert.True(TripService.NearestStop(Route, 23.7102, 90.4001)!.Value.Km < 0.1);
        // B is closest, but only A and C may be chosen.
        Assert.Equal(2, TripService.NearestStop(Route, 23.7160, 90.40, i => i != 1)!.Value.Index);
        Assert.Null(TripService.NearestStop(Route, 23.71, 90.40, _ => false));
    }

    [Theory]
    [InlineData(23.7, 90.4, true)]
    [InlineData(0.0, 0.0, false)]   // "no location" sent as zeros
    [InlineData(91.0, 90.4, false)]
    [InlineData(23.7, null, false)]
    public void Only_real_points_count_as_a_location(double? lat, double? lng, bool real) =>
        Assert.Equal(real, TripService.ValidLat(lat, lng) is not null);
}

// QR boarding end to end against PostgreSQL (skipped without SMARTCOMMUTE_TEST_DB).
[Collection(ServerCollection.Name)]
public class BusQrFlowTests(TestServerFixture server)
{
    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    private async Task<(Guid Route, Guid Vehicle)> NewVehicle(string name)
    {
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bus = new Bus
        {
            BusName = name, RouteNumber = $"Q-{Guid.NewGuid():N}"[..20], BaseFare = 10, PerKmFare = 2.5m, RunsBothWays = true,
            Stops = new[] { "Jatrabari", "Malibagh", "Rampura", "Badda" }.Select((stop, i) => new BusStop { Name = stop, Order = i, Lat = 23.70 + i * 0.01, Lng = 90.40 }).ToList(),
        };
        var vehicle = new IndividualBus { Bus = bus, BusCode = $"Q-{Guid.NewGuid():N}"[..12], TotalPassengerCapacity = 40 };
        db.IndividualBuses.Add(vehicle);
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<BusRouteCache>().Invalidate();
        return (bus.Id, vehicle.Id);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string> Generate(HttpClient admin, Guid vehicle)
    {
        var response = await admin.PostAsync($"/bus/{vehicle}/generate-qr", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("data").GetProperty("token").GetString()!;
    }

    private static async Task<string> Verify(HttpClient client, string code)
    {
        var response = await client.PostAsJsonAsync("/bus/verify-qr", new { code });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response)).GetProperty("data").GetProperty("scanTicket").GetString()!;
    }

    [SkippableFact]
    public async Task Only_admins_and_the_bus_driver_manage_codes()
    {
        RequireServer();
        var (_, vehicle) = await NewVehicle("Code Paribahan");
        var (_, rider) = await server.CreateUserAsync();
        var (_, admin) = await server.CreateAdminAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await rider.PostAsync($"/bus/{vehicle}/generate-qr", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await rider.GetAsync($"/bus/{vehicle}/qr")).StatusCode);

        var token = await Generate(admin, vehicle);
        var shown = await Json(await admin.GetAsync($"/bus/{vehicle}/qr"));
        Assert.Equal(token, shown.GetProperty("data").GetProperty("token").GetString());

        // The token is never stored as it is.
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.BusQrCodes.SingleAsync(c => c.IndividualBusId == vehicle && c.IsActive);
        Assert.Equal(BusQrService.Hash(token), stored.TokenHash);
        Assert.DoesNotContain(token, stored.TokenCipher);
    }

    [SkippableFact]
    public async Task Scanning_boards_once_and_ending_by_location_settles_the_fare()
    {
        RequireServer();
        var (route, vehicle) = await NewVehicle("Scan Paribahan");
        var (_, admin) = await server.CreateAdminAsync();
        var (user, rider) = await server.CreateUserAsync();
        var token = await Generate(admin, vehicle);

        var scan = await Json(await rider.PostAsJsonAsync("/bus/verify-qr", new { code = $"https://site.example/scan?c={token}", lat = 23.7101, lng = 90.4001 }));
        Assert.Equal("Malibagh", scan.GetProperty("data").GetProperty("nearestStop").GetString());
        var ticket = scan.GetProperty("data").GetProperty("scanTicket").GetString()!;

        // A stop the bus does not serve is refused, and the same scan can still be used afterwards.
        Assert.Equal(HttpStatusCode.BadRequest, (await rider.PostAsJsonAsync("/trip/board-by-qr", new { scanTicket = ticket, boardingStop = "Gulistan" })).StatusCode);

        var boarded = await rider.PostAsJsonAsync("/trip/board-by-qr", new { scanTicket = ticket, boardingStop = "Malibagh", lat = 23.7101, lng = 90.4001 });
        Assert.Equal(HttpStatusCode.Created, boarded.StatusCode);
        var trip = (await Json(boarded)).GetProperty("data");
        Assert.Equal("qr", trip.GetProperty("boardingMethod").GetString());
        Assert.Equal(route.ToString(), trip.GetProperty("busId").GetString());

        // Sent again: the same trip, not a second one.
        Assert.Equal(HttpStatusCode.OK, (await rider.PostAsJsonAsync("/trip/board-by-qr", new { scanTicket = ticket, boardingStop = "Malibagh" })).StatusCode);
        // Another rider cannot use this rider's ticket.
        var (_, other) = await server.CreateUserAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync("/trip/board-by-qr", new { scanTicket = ticket, boardingStop = "Malibagh" })).StatusCode);

        var ended = await rider.PostAsJsonAsync("/trip/end", new { lat = 23.7301, lng = 90.4001 });
        Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        var done = await Json(ended);
        Assert.Equal("Badda", done.GetProperty("data").GetProperty("destination").GetString());
        Assert.True(done.GetProperty("data").GetProperty("fare").GetDecimal() >= 10);
        Assert.True(done.GetProperty("gemsEarned").GetInt32() > 0);
        Assert.Equal(HttpStatusCode.BadRequest, (await rider.PostAsJsonAsync("/trip/end", new { })).StatusCode);

        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Trips.CountAsync(t => t.UserId == user.Id));
        Assert.NotNull((await db.BusQrScans.SingleAsync(s => s.UserId == user.Id)).TripId);
    }

    [SkippableFact]
    public async Task Old_and_turned_off_codes_no_longer_board()
    {
        RequireServer();
        var (_, vehicle) = await NewVehicle("Retired Paribahan");
        var (_, admin) = await server.CreateAdminAsync();
        var (_, rider) = await server.CreateUserAsync();

        var old = await Generate(admin, vehicle);
        var current = await Generate(admin, vehicle);
        Assert.Equal(HttpStatusCode.Gone, (await rider.PostAsJsonAsync("/bus/verify-qr", new { code = old })).StatusCode);
        var ticket = await Verify(rider, current);

        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/bus/{vehicle}/qr")).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await rider.PostAsJsonAsync("/bus/verify-qr", new { code = current })).StatusCode);
        // A scan made before the code was turned off cannot board either.
        Assert.Equal(HttpStatusCode.Gone, (await rider.PostAsJsonAsync("/trip/board-by-qr", new { scanTicket = ticket, boardingStop = "Malibagh" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await rider.PostAsJsonAsync("/bus/verify-qr", new { code = BusQrService.NewSecret() })).StatusCode);
    }

    [SkippableFact]
    public async Task Two_confirmations_sent_at_once_start_one_trip()
    {
        RequireServer();
        var (_, vehicle) = await NewVehicle("Twice Paribahan");
        var (_, admin) = await server.CreateAdminAsync();
        var (user, rider) = await server.CreateUserAsync();
        var ticket = await Verify(rider, await Generate(admin, vehicle));

        var answers = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            rider.PostAsJsonAsync("/trip/board-by-qr", new { scanTicket = ticket, boardingStop = "Jatrabari" })));
        Assert.Contains(answers, a => a.StatusCode == HttpStatusCode.Created);

        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Trips.CountAsync(t => t.UserId == user.Id));
        Assert.Equal(1, (await db.IndividualBuses.SingleAsync(v => v.Id == vehicle)).CurrentPassengerCount);
    }
}
