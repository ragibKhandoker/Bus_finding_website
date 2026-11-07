using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

// The buses that serve a trip, with the stops the typed places resolved to.
public record RouteSearch(List<BusViewModel> Buses, BusStopViewModel Start, BusStopViewModel End);

// Bus routes and vehicles: searching by name or by trip, route statistics and road estimates. Routes are read
// from the in-memory BusRouteCache, so no user text goes into SQL.
public class BusService(AppDbContext db, BusRouteCache routes, GoogleRoutesService google, GeoService geo, IMemoryCache cache)
{
    public Task<List<BusViewModel>> AllAsync(CancellationToken ct) => routes.AllAsync(ct);

    public Task<BusViewModel?> ByRouteNumberAsync(string routeNumber, CancellationToken ct) => routes.ByRouteNumberAsync(routeNumber, ct);

    // How many routes and distinct stops are stored.
    public async Task<(int Routes, int Stops)> CountAsync(CancellationToken ct)
    {
        var buses = await routes.AllAsync(ct);
        return (buses.Count, buses.SelectMany(b => b.Stops).Select(s => s.Name).Distinct().Count());
    }

    // Case-insensitive partial match on the bus name, route number or operator ("brt" finds "BRTC").
    public async Task<ServiceResult<List<BusViewModel>>> SearchByNameAsync(string? busName, CancellationToken ct)
    {
        var term = BusRouteMatcher.Normalize(busName ?? "");
        if (term.Length == 0)
        {
            return ServiceResult<List<BusViewModel>>.BadRequest("Enter a bus name to search.");
        }
        var buses = (await routes.AllAsync(ct))
            .Where(b => new[] { b.BusName, b.RouteNumber, b.Operator }.Any(text => text is not null && BusRouteMatcher.Normalize(text).Contains(term)))
            .OrderByDescending(b => BusRouteMatcher.Normalize(b.BusName).StartsWith(term))
            .ThenBy(b => b.BusName)
            .ToList();
        return ServiceResult<List<BusViewModel>>.Ok(buses);
    }

    // Buses that stop at both places with the start first in the stored stop order, or in reverse order when the
    // bus runs the return trip too (see BusRouteMatcher). Each result says where to board and get off.
    public async Task<ServiceResult<RouteSearch>> SearchByRouteAsync(string? startLocation, string? endLocation, CancellationToken ct)
    {
        var all = await routes.AllAsync(ct);
        var resolved = ResolveTrip(all, startLocation, endLocation);
        if (!resolved.Succeeded)
        {
            return new(resolved.StatusCode, resolved.Message);
        }
        var (start, end) = resolved.Value;
        var buses = all
            .Select(b => (bus: b, match: BusRouteMatcher.Match(b, start, end)))
            .Where(x => x.match is not null)
            .OrderBy(x => x.match!.Direction == "forward" ? 0 : 1)
            .ThenBy(x => x.match!.StopsToRide)
            .ThenBy(x => x.bus.BusName)
            .Select(x => x.bus.WithMatch(x.match!))
            .ToList();
        return ServiceResult<RouteSearch>.Ok(new RouteSearch(buses, start.Stop, end.Stop));
    }

    // Road distance and travel time between two places. With a Google server key (GoogleMaps:ServerApiKey) this is
    // Google's Routes API, with traffic and the road line for the map. Without one it is the free OpenStreetMap
    // router (OSRM) that GeoService already uses, so the site works with no Google setup at all.
    // Either way it is a driving route, not the path of any bus. Places are resolved to bus stops, so the services
    // are only ever asked about trips between stops in the database.
    public async Task<ServiceResult<RouteEstimateViewModel>> RouteEstimateAsync(string? startLocation, string? endLocation, CancellationToken ct)
    {
        var all = await routes.AllAsync(ct);
        var resolved = ResolveTrip(all, startLocation, endLocation);
        if (!resolved.Succeeded)
        {
            return new(resolved.StatusCode, resolved.Message);
        }
        var (start, end) = resolved.Value;

        if (!google.IsConfigured)
        {
            // OSRM's public server asks for light use, so each trip is looked up once an hour at most.
            var key = $"osrm:{start.Stop.Name}:{end.Stop.Name}";
            var road = await cache.GetOrCreateAsync(key, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                return geo.RoadDistanceAsync(start.Stop.Lat, start.Stop.Lng, end.Stop.Lat, end.Stop.Lng, ct);
            });
            if (road!.Estimated)
            {
                // GeoService fell back to a straight-line guess; do not keep it, the router may be back next time.
                cache.Remove(key);
            }
            return ServiceResult<RouteEstimateViewModel>.Ok(new RouteEstimateViewModel(
                Math.Round(road.DistanceKm, 1), Math.Round(road.DurationMinutes), null, null,
                start.Stop, end.Stop, road.Estimated ? "approximate" : "openstreetmap"));
        }

        try
        {
            var estimate = await google.DrivingRouteAsync(start.Stop.Lat, start.Stop.Lng, end.Stop.Lat, end.Stop.Lng, ct);
            return ServiceResult<RouteEstimateViewModel>.Ok(new RouteEstimateViewModel(
                estimate.DistanceKm, estimate.DurationMinutes, estimate.StaticDurationMinutes, estimate.Polyline,
                start.Stop, end.Stop, "google-routes"));
        }
        catch (GoogleRoutesException ex)
        {
            return new(ex.StatusCode, ex.Message);
        }
    }

    // A route with its details and the physical vehicles running on it.
    public async Task<ServiceResult<(BusViewModel Bus, List<IndividualBus> Vehicles)>> RouteWithVehiclesAsync(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var busId))
        {
            return ServiceResult<(BusViewModel, List<IndividualBus>)>.BadRequest("Invalid bus id.");
        }
        var bus = (await routes.AllAsync(ct)).FirstOrDefault(b => b.Id == busId.ToString());
        if (bus is null)
        {
            return ServiceResult<(BusViewModel, List<IndividualBus>)>.NotFound("Bus not found.");
        }
        var vehicles = await db.IndividualBuses
            .Where(b => b.BusId == busId)
            .OrderBy(b => b.BusCode)
            .AsNoTracking()
            .ToListAsync(ct);
        return ServiceResult<(BusViewModel, List<IndividualBus>)>.Ok((bus, vehicles));
    }

    public Task<IndividualBus?> VehicleAsync(Guid id) => db.IndividualBuses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);

    // Checks both places and resolves them to stops.
    private static ServiceResult<(ResolvedPlace Start, ResolvedPlace End)> ResolveTrip(List<BusViewModel> all, string? startLocation, string? endLocation)
    {
        if (string.IsNullOrWhiteSpace(startLocation))
        {
            return ServiceResult<(ResolvedPlace, ResolvedPlace)>.BadRequest("Enter your starting point.");
        }
        if (string.IsNullOrWhiteSpace(endLocation))
        {
            return ServiceResult<(ResolvedPlace, ResolvedPlace)>.BadRequest("Enter your destination.");
        }
        var start = BusRouteMatcher.Resolve(all, startLocation);
        if (start is null)
        {
            return ServiceResult<(ResolvedPlace, ResolvedPlace)>.NotFound($"No bus stop matches \"{startLocation.Trim()}\". Check the spelling or pick a stop from the list.");
        }
        var end = BusRouteMatcher.Resolve(all, endLocation);
        if (end is null)
        {
            return ServiceResult<(ResolvedPlace, ResolvedPlace)>.NotFound($"No bus stop matches \"{endLocation.Trim()}\". Check the spelling or pick a stop from the list.");
        }
        if (start.Names.SetEquals(end.Names))
        {
            return ServiceResult<(ResolvedPlace, ResolvedPlace)>.BadRequest("Your starting point and destination are the same.");
        }
        return ServiceResult<(ResolvedPlace, ResolvedPlace)>.Ok((start, end));
    }
}
