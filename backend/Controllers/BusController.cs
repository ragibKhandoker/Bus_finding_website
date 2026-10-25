using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Extensions;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

[Authorize]
[Route("bus")]
public class BusController(AppDbContext db, BusRouteCache routes) : ApiControllerBase
{
    // Every route with its stops in order, served from memory. Public route information, shown to visitors too.
    [AllowAnonymous]
    [HttpGet("all")]
    public async Task<IActionResult> All()
    {
        var buses = await routes.AllAsync(HttpContext.RequestAborted);
        return Ok(new { success = true, count = buses.Count, data = buses });
    }

    // One route with its stops, for the route's own page. Route numbers are matched without regard to case.
    [AllowAnonymous]
    [HttpGet("route/{routeNumber}")]
    public async Task<IActionResult> ByRouteNumber(string routeNumber)
    {
        var bus = await routes.ByRouteNumberAsync(routeNumber, HttpContext.RequestAborted);
        return bus is null
            ? NotFoundError("Bus route not found.")
            : Ok(new { success = true, data = bus });
    }

    // How many routes and distinct stops are stored, for the home page. Open to visitors who are not signed in.
    [AllowAnonymous]
    [HttpGet("count")]
    public async Task<IActionResult> Count()
    {
        try
        {
            var buses = await routes.AllAsync(HttpContext.RequestAborted);
            var stops = buses.SelectMany(b => b.Stops).Select(s => s.Name).Distinct().Count();
            return Ok(new { success = true, routes = buses.Count, stops });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "Bus data is unavailable right now.");
        }
    }

    // Case-insensitive partial match on the bus name, route number or operator ("brt" finds "BRTC").
    // Searched in the in-memory route list, so no user text goes into SQL.
    [HttpGet("search-by-name")]
    public async Task<IActionResult> SearchByName([FromQuery] string? busName)
    {
        var term = BusRouteMatcher.Normalize(busName ?? "");
        if (term.Length == 0)
        {
            return BadRequestError("Enter a bus name to search.");
        }

        var buses = (await routes.AllAsync(HttpContext.RequestAborted))
            .Where(b => new[] { b.BusName, b.RouteNumber, b.Operator }.Any(text => text is not null && BusRouteMatcher.Normalize(text).Contains(term)))
            .OrderByDescending(b => BusRouteMatcher.Normalize(b.BusName).StartsWith(term))
            .ThenBy(b => b.BusName)
            .ToList();
        return Ok(new { success = true, count = buses.Count, data = buses });
    }

    // Buses that stop at both places with the start first in the stored stop order, or in reverse order when the
    // bus runs the return trip too (see BusRouteMatcher). Each result says where to board and get off.
    // start and end are the stops the typed places resolved to, for the map.
    [HttpGet("search-by-route")]
    public async Task<IActionResult> SearchByRoute([FromQuery] string? startLocation, [FromQuery] string? endLocation)
    {
        var all = await routes.AllAsync(HttpContext.RequestAborted);
        var (start, end, problem) = ResolveTrip(all, startLocation, endLocation);
        if (problem is not null)
        {
            return problem;
        }

        var buses = all
            .Select(b => (bus: b, match: BusRouteMatcher.Match(b, start!, end!)))
            .Where(x => x.match is not null)
            .OrderBy(x => x.match!.Direction == "forward" ? 0 : 1)
            .ThenBy(x => x.match!.StopsToRide)
            .ThenBy(x => x.bus.BusName)
            .Select(x => x.bus.WithMatch(x.match!))
            .ToList();
        return Ok(new { success = true, count = buses.Count, data = buses, start = start!.Stop, end = end!.Stop });
    }

    // Road distance and travel time between two places. With a Google server key (GoogleMaps:ServerApiKey) this is
    // Google's Routes API, with traffic and the road line for the map. Without one it is the free OpenStreetMap
    // router (OSRM) that GeoService already uses, so the site works with no Google setup at all.
    // Either way it is a driving route, not the path of any bus. Places are resolved to bus stops, so the services
    // are only ever asked about trips between stops in the database.
    [EnableRateLimiting("maps")]
    [HttpGet("route-estimate")]
    public async Task<IActionResult> RouteEstimate([FromQuery] string? startLocation, [FromQuery] string? endLocation,
        [FromServices] GoogleRoutesService google, [FromServices] GeoService geo, [FromServices] IMemoryCache cache)
    {
        var all = await routes.AllAsync(HttpContext.RequestAborted);
        var (start, end, problem) = ResolveTrip(all, startLocation, endLocation);
        if (problem is not null)
        {
            return problem;
        }

        if (!google.IsConfigured)
        {
            // OSRM's public server asks for light use, so each trip is looked up once an hour at most.
            var key = $"osrm:{start!.Stop.Name}:{end!.Stop.Name}";
            var road = await cache.GetOrCreateAsync(key, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                return geo.RoadDistanceAsync(start.Stop.Lat, start.Stop.Lng, end.Stop.Lat, end.Stop.Lng, HttpContext.RequestAborted);
            });
            if (road!.Estimated)
            {
                // GeoService fell back to a straight-line guess; do not keep it, the router may be back next time.
                cache.Remove(key);
            }
            return Ok(new
            {
                success = true,
                data = new
                {
                    distanceKm = Math.Round(road.DistanceKm, 1),
                    durationMinutes = Math.Round(road.DurationMinutes),
                    durationWithoutTrafficMinutes = (double?)null,
                    polyline = (string?)null,
                    start = start.Stop,
                    end = end.Stop,
                    // "approximate" when the router was unreachable and the distance is a straight-line estimate.
                    source = road.Estimated ? "approximate" : "openstreetmap",
                },
            });
        }

        try
        {
            var estimate = await google.DrivingRouteAsync(start!.Stop.Lat, start.Stop.Lng, end!.Stop.Lat, end.Stop.Lng, HttpContext.RequestAborted);
            return Ok(new
            {
                success = true,
                data = new
                {
                    distanceKm = estimate.DistanceKm,
                    durationMinutes = estimate.DurationMinutes,
                    durationWithoutTrafficMinutes = estimate.StaticDurationMinutes,
                    polyline = estimate.Polyline,
                    start = start.Stop,
                    end = end.Stop,
                    source = "google-routes",
                },
            });
        }
        catch (GoogleRoutesException ex)
        {
            return Error(ex.StatusCode, ex.Message);
        }
    }

    // Checks both places and resolves them to stops; problem is the error response when that fails.
    private (ResolvedPlace? Start, ResolvedPlace? End, IActionResult? Problem) ResolveTrip(List<BusViewModel> all, string? startLocation, string? endLocation)
    {
        if (string.IsNullOrWhiteSpace(startLocation))
        {
            return (null, null, BadRequestError("Enter your starting point."));
        }
        if (string.IsNullOrWhiteSpace(endLocation))
        {
            return (null, null, BadRequestError("Enter your destination."));
        }
        var start = BusRouteMatcher.Resolve(all, startLocation);
        if (start is null)
        {
            return (null, null, NotFoundError($"No bus stop matches \"{startLocation.Trim()}\". Check the spelling or pick a stop from the list."));
        }
        var end = BusRouteMatcher.Resolve(all, endLocation);
        if (end is null)
        {
            return (null, null, NotFoundError($"No bus stop matches \"{endLocation.Trim()}\". Check the spelling or pick a stop from the list."));
        }
        if (start.Names.SetEquals(end.Names))
        {
            return (null, null, BadRequestError("Your starting point and destination are the same."));
        }
        return (start, end, null);
    }

    // A route with its details and ordered stops, and the physical vehicles running on it.
    [HttpGet("individual/{id}")]
    public async Task<IActionResult> Individual(string id)
    {
        if (!Guid.TryParse(id, out var busId))
        {
            return BadRequestError("Invalid bus id.");
        }
        var bus = (await routes.AllAsync(HttpContext.RequestAborted)).FirstOrDefault(b => b.Id == busId.ToString());
        if (bus is null)
        {
            return NotFoundError("Bus not found.");
        }

        var vehicles = await db.IndividualBuses
            .Where(b => b.BusId == busId)
            .OrderBy(b => b.BusCode)
            .AsNoTracking()
            .ToListAsync();
        return Ok(new { success = true, bus, data = vehicles.Select(IndividualBusViewModel.From) });
    }
}

[Authorize]
[Route("individual-bus")]
public class IndividualBusController(AppDbContext db) : ApiControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var bus = await db.IndividualBuses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
        return bus is null
            ? NotFoundError("Bus not found.")
            : Ok(new { success = true, data = IndividualBusViewModel.From(bus) });
    }

    [HttpPost("board")]
    public async Task<IActionResult> Board(BusIdRequest request)
    {
        var bus = await db.IndividualBuses.FindAsync(request.BusId);
        if (bus is null)
        {
            return NotFoundError("Bus not found.");
        }

        var userId = CurrentUserId;
        var active = await db.BusBoardings.Include(x => x.IndividualBus)
            .FirstOrDefaultAsync(x => x.UserId == userId && x.EndedAt == null);
        if (active?.IndividualBusId == bus.Id)
        {
            return Ok(new { success = true, message = "You are already on this bus.", data = IndividualBusViewModel.From(bus) });
        }
        if (bus.CurrentPassengerCount >= bus.TotalPassengerCapacity)
        {
            return BadRequestError("This bus is full.");
        }

        // A rider can only be on one bus; leaving the previous one frees that seat.
        if (active is not null)
        {
            active.EndedAt = DateTime.UtcNow;
            active.IndividualBus!.CurrentPassengerCount = Math.Max(0, active.IndividualBus.CurrentPassengerCount - 1);
        }

        bus.CurrentPassengerCount++;
        db.BusBoardings.Add(new BusBoarding { UserId = userId, IndividualBusId = bus.Id });
        await db.SaveChangesAsync();
        return Ok(new { success = true, message = "Boarded successfully.", data = IndividualBusViewModel.From(bus) });
    }

    [HttpPost("end-trip")]
    public async Task<IActionResult> EndTrip(BusIdRequest request)
    {
        var userId = CurrentUserId;
        var boarding = await db.BusBoardings.Include(x => x.IndividualBus)
            .FirstOrDefaultAsync(x => x.UserId == userId && x.EndedAt == null && x.IndividualBusId == request.BusId);
        if (boarding is null)
        {
            return BadRequestError("You are not currently on this bus.");
        }

        boarding.EndedAt = DateTime.UtcNow;
        var bus = boarding.IndividualBus!;
        bus.CurrentPassengerCount = Math.Max(0, bus.CurrentPassengerCount - 1);
        await db.SaveChangesAsync();
        return Ok(new { success = true, message = "Trip ended.", data = IndividualBusViewModel.From(bus) });
    }
}
