using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SmartCommute.Server.Data;
using SmartCommute.Server.Extensions;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

// The buses that serve a trip, with the stops the typed places resolved to.
public record RouteSearch(List<BusViewModel> Buses, BusStopViewModel Start, BusStopViewModel End);

// A bus that serves a trip between two map points: the bus (with where to board and get off), the walk from the
// starting point to the boarding stop and from the last stop to the destination, and the ride on the bus.
public record NearbyBusOption(BusViewModel Bus, double WalkToBoardingKm, double WalkFromAlightingKm, double RideKm, decimal Fare);

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

    public const double DefaultWalkKm = 1.0;

    // Buses between two points on the map (places picked on the fare calculator, which have coordinates but are
    // not stop names): every stop within walking distance of each point counts, then the usual route rules apply
    // (BusRouteMatcher). Sorted by the least walking, then the fewest stops. Each bus's fare follows its own rates,
    // like a boarded trip (TripService.Fare).
    public async Task<ServiceResult<List<NearbyBusOption>>> SearchNearAsync(double fromLat, double fromLng, double toLat, double toLng,
        double walkKm, CancellationToken ct)
    {
        var radius = Math.Clamp(walkKm, 0.2, 3);
        var all = await routes.AllAsync(ct);
        var stops = all.SelectMany(r => r.Stops).ToList();
        ResolvedPlace? Near(double lat, double lng)
        {
            var close = stops.Select(s => (stop: s, km: GeoService.HaversineKm(lat, lng, s.Lat, s.Lng))).Where(x => x.km <= radius).ToList();
            return close.Count == 0
                ? null
                : new ResolvedPlace(close.Select(x => BusRouteMatcher.Normalize(x.stop.Name)).ToHashSet(), close.MinBy(x => x.km).stop);
        }
        var start = Near(fromLat, fromLng);
        var end = Near(toLat, toLng);
        if (start is null || end is null)
        {
            return ServiceResult<List<NearbyBusOption>>.NotFound(
                $"There is no bus stop within {radius:0.#} km of the {(start is null ? "starting point" : "destination")}.");
        }

        var options = all
            .Select(bus => (bus, match: BusRouteMatcher.Match(bus, start, end)))
            .Where(x => x.match is not null)
            .Select(x =>
            {
                var match = x.match!;
                var rideKm = match.Segment.Zip(match.Segment.Skip(1), (a, b) => GeoService.HaversineKm(a.Lat, a.Lng, b.Lat, b.Lng)).Sum();
                return new NearbyBusOption(
                    x.bus.WithMatch(match),
                    Math.Round(GeoService.HaversineKm(fromLat, fromLng, match.Boarding.Lat, match.Boarding.Lng), 2),
                    Math.Round(GeoService.HaversineKm(match.Alighting.Lat, match.Alighting.Lng, toLat, toLng), 2),
                    Math.Round(rideKm, 2),
                    TripService.Fare(x.bus, rideKm));
            })
            .OrderBy(o => o.WalkToBoardingKm + o.WalkFromAlightingKm)
            .ThenBy(o => o.Bus.Match!.StopsToRide)
            .ThenBy(o => o.Bus.BusName)
            .ToList();
        return ServiceResult<List<NearbyBusOption>>.Ok(options);
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

    // A vehicle by its id, or by its code as printed on the bus ("AL-2-01", any case). Riders only ever see the
    // code, so lookups by hand use that. The route comes along for its name and number.
    public Task<IndividualBus?> VehicleByIdOrCodeAsync(string idOrCode)
    {
        var key = idOrCode.Trim();
        var vehicles = db.IndividualBuses.Include(b => b.Bus).AsNoTracking();
        return Guid.TryParse(key, out var id)
            ? vehicles.FirstOrDefaultAsync(b => b.Id == id)
            : vehicles.FirstOrDefaultAsync(b => b.BusCode.ToUpper() == key.ToUpper());
    }

    // Vehicle codes containing the text, codes that start with it first, for suggestions while typing.
    public Task<List<IndividualBus>> SearchVehiclesAsync(string text, int limit)
    {
        var pattern = text.Trim().ContainsPattern();
        var starts = text.Trim().ToUpper();
        return db.IndividualBuses.Include(b => b.Bus).AsNoTracking()
            .Where(b => EF.Functions.ILike(b.BusCode, pattern))
            .OrderByDescending(b => b.BusCode.ToUpper().StartsWith(starts))
            .ThenBy(b => b.BusCode)
            .Take(Math.Clamp(limit, 1, 20))
            .ToListAsync();
    }

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
