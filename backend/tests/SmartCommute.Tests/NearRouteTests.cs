using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Tests;

// "Which bus did you take?" on the fare calculator: buses between two map points, and logging a trip on one.
[Collection(ServerCollection.Name)]
public class NearRouteTests(TestServerFixture server)
{
    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    // A route far from Dhaka's real stops (in the Bay of Bengal), so other routes never match: stops 1 km apart.
    private const double BaseLat = 21.0, Lng = 89.0, Step = 0.009;

    private async Task<(Guid RouteId, string Name)> NewRoute(bool bothWays = false)
    {
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var name = $"Sea {Guid.NewGuid():N}"[..12];
        var bus = new Bus
        {
            BusName = name, RouteNumber = $"S-{Guid.NewGuid():N}"[..12], BaseFare = 10, PerKmFare = 2.5m, RunsBothWays = bothWays,
            Stops = Enumerable.Range(0, 5).Select(i => new BusStop { Name = $"{name} Stop {i}", Order = i, Lat = BaseLat + i * Step, Lng = Lng }).ToList(),
        };
        db.Buses.Add(bus);
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<BusRouteCache>().Invalidate();
        return (bus.Id, name);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    [SkippableFact]
    public async Task Finds_the_bus_between_two_points_with_walks_ride_and_fare()
    {
        RequireServer();
        var (routeId, name) = await NewRoute();
        var (_, client) = await server.CreateUserAsync();

        // 100 m from stop 0 to 100 m from stop 3.
        var r = await client.GetAsync($"/bus/near-route?fromLat={BaseLat + 0.0009}&fromLng={Lng}&toLat={BaseLat + 3 * Step + 0.0009}&toLng={Lng}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var option = Assert.Single((await Json(r)).GetProperty("data").EnumerateArray(), o => o.GetProperty("bus").GetProperty("_id").GetString() == routeId.ToString());
        Assert.Equal($"{name} Stop 0", option.GetProperty("bus").GetProperty("match").GetProperty("boarding").GetProperty("name").GetString());
        Assert.Equal($"{name} Stop 3", option.GetProperty("bus").GetProperty("match").GetProperty("alighting").GetProperty("name").GetString());
        Assert.InRange(option.GetProperty("walkToBoardingKm").GetDouble(), 0.05, 0.15);
        Assert.InRange(option.GetProperty("rideKm").GetDouble(), 2.9, 3.1);
        Assert.Equal(10m, option.GetProperty("fare").GetDecimal()); // 3 km × 2.5 = 7.5, below the 10 Tk minimum
    }

    [SkippableFact]
    public async Task Refuses_a_one_way_bus_backwards_and_points_with_no_stop_nearby()
    {
        RequireServer();
        var (routeId, _) = await NewRoute(bothWays: false);
        var (_, client) = await server.CreateUserAsync();

        var backwards = await Json(await client.GetAsync($"/bus/near-route?fromLat={BaseLat + 3 * Step}&fromLng={Lng}&toLat={BaseLat}&toLng={Lng}"));
        Assert.DoesNotContain(backwards.GetProperty("data").EnumerateArray(), o => o.GetProperty("bus").GetProperty("_id").GetString() == routeId.ToString());

        var farAway = await client.GetAsync($"/bus/near-route?fromLat={BaseLat}&fromLng={Lng}&toLat={BaseLat + 1}&toLng={Lng + 1}");
        Assert.Equal(HttpStatusCode.NotFound, farAway.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/bus/near-route?fromLat=200&fromLng=0&toLat=0&toLng=0")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.ClientFor(null).GetAsync($"/bus/near-route?fromLat={BaseLat}&fromLng={Lng}&toLat={BaseLat}&toLng={Lng}")).StatusCode);
    }

    [SkippableFact]
    public async Task Logging_a_trip_on_a_chosen_bus_saves_the_route_and_its_own_fare()
    {
        RequireServer();
        var (routeId, name) = await NewRoute();
        var (user, client) = await server.CreateUserAsync();

        // The client's distance and fare are replaced by the route's own.
        var r = await client.PostAsJsonAsync("/trip/add", new { busId = routeId.ToString(), busName = "anything", distance = 99, fare = 999, source = $"{name} Stop 1", destination = $"{name} Stop 4" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);

        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var trip = await db.Trips.SingleAsync(t => t.UserId == user.Id);
        Assert.Equal(name, trip.BusName);
        Assert.NotNull(trip.RouteNumber);
        Assert.InRange(trip.Distance, 2.9, 3.1);
        Assert.Equal(10m, trip.Fare);
        Assert.Equal(new[] { $"{name} Stop 1", $"{name} Stop 2", $"{name} Stop 3", $"{name} Stop 4" }, trip.StopsRidden);
    }
}
