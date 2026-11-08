using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Tests;

// A rider is on at most one bus at a time: boarding another bus is refused until the current trip is ended.
[Collection(ServerCollection.Name)]
public class OneBusAtATimeTests(TestServerFixture server)
{
    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    // A route of its own with one vehicle, so seats and trips are not shared between tests.
    private async Task<Guid> NewVehicle(string name)
    {
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bus = new Bus
        {
            BusName = name, RouteNumber = $"T-{Guid.NewGuid():N}"[..20], BaseFare = 10, PerKmFare = 2.5m, RunsBothWays = true,
            Stops = new[] { "Jatrabari", "Malibagh", "Rampura", "Badda" }.Select((stop, i) => new BusStop { Name = stop, Order = i, Lat = 23.70 + i * 0.01, Lng = 90.40 }).ToList(),
        };
        var vehicle = new IndividualBus { Bus = bus, BusCode = $"V-{Guid.NewGuid():N}"[..12], TotalPassengerCapacity = 40 };
        db.IndividualBuses.Add(vehicle);
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<BusRouteCache>().Invalidate();
        return vehicle.Id;
    }

    private static Task<HttpResponseMessage> Board(HttpClient client, Guid vehicle) =>
        client.PostAsJsonAsync($"/individual-bus/{vehicle}/board", new { boardingStop = "Malibagh" });

    private static Task<HttpResponseMessage> End(HttpClient client, Guid vehicle) =>
        client.PostAsJsonAsync($"/individual-bus/{vehicle}/end-trip", new { destination = "Badda" });

    private static async Task<string> Message(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString()!;

    [SkippableFact]
    public async Task Another_bus_cannot_be_boarded_until_the_trip_is_ended()
    {
        RequireServer();
        var first = await NewVehicle("First Paribahan");
        var second = await NewVehicle("Second Paribahan");
        var (user, client) = await server.CreateUserAsync();

        Assert.Equal(HttpStatusCode.Created, (await Board(client, first)).StatusCode);

        var refused = await Board(client, second);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("You already have an active trip on First Paribahan. End it before boarding another bus.", await Message(refused));

        // Boarding the same bus again is not a second trip.
        Assert.Equal(HttpStatusCode.OK, (await Board(client, first)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await End(client, first)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Board(client, second)).StatusCode);

        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Trips.CountAsync(t => t.UserId == user.Id && t.Status == TripStatus.Active));
        Assert.Equal(1, await db.Trips.CountAsync(t => t.UserId == user.Id && t.Status == TripStatus.Completed));
        // The refused boarding did not take a seat.
        Assert.Equal(1, (await db.IndividualBuses.SingleAsync(v => v.Id == second)).CurrentPassengerCount);
        Assert.Equal(0, (await db.IndividualBuses.SingleAsync(v => v.Id == first)).CurrentPassengerCount);
    }

    [SkippableFact]
    public async Task Simultaneous_boardings_on_two_buses_leave_one_trip()
    {
        RequireServer();
        var first = await NewVehicle("Left Paribahan");
        var second = await NewVehicle("Right Paribahan");
        var (user, client) = await server.CreateUserAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Board(client, i % 2 == 0 ? first : second)));
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.Conflict, $"Unexpected {r.StatusCode}"));

        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Trips.CountAsync(t => t.UserId == user.Id && t.Status == TripStatus.Active));
        var seats = await db.IndividualBuses.Where(v => v.Id == first || v.Id == second).SumAsync(v => v.CurrentPassengerCount);
        Assert.Equal(1, seats);
    }

    [SkippableFact]
    public async Task Each_rider_has_their_own_one_bus_limit()
    {
        RequireServer();
        var first = await NewVehicle("Shared Paribahan");
        var second = await NewVehicle("Other Paribahan");
        var (_, a) = await server.CreateUserAsync();
        var (_, b) = await server.CreateUserAsync();

        Assert.Equal(HttpStatusCode.Created, (await Board(a, first)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Board(b, second)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Board(a, second)).StatusCode);
    }
}
