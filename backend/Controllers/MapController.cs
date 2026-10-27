using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Extensions;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// Place search goes to OpenStreetMap Nominatim, whose usage policy forbids search-as-you-type: clients call
// search when the user submits a query, and use /api/stops/search (our own database) for live suggestions.
[Route("api/geocoding")]
[EnableRateLimiting("maps")]
public class GeocodingController(GeoService geo, FareService fares, ILogger<GeocodingController> logger) : ApiControllerBase
{
    // dhaka=true keeps results inside greater Dhaka; lang=en|bn picks the language of the place names.
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] int limit = 5,
        [FromQuery] string countrycodes = "bd", [FromQuery] bool dhaka = false, [FromQuery] string? lang = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequestError("q is required.");
        }
        if (q.Trim().Length < 2 || q.Length > 200)
        {
            return BadRequestError("Type between 2 and 200 characters to search.");
        }
        try
        {
            var places = await geo.SearchAsync(q, limit, countrycodes, ct, dhaka, lang);
            return Ok(new { success = true, data = places.Select(ToJson) });
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Place search failed");
            return Error(StatusCodes.Status502BadGateway, "Place search is unavailable right now.");
        }
    }

    [HttpGet("geocode")]
    public async Task<IActionResult> Geocode([FromQuery] string? q, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequestError("q is required.");
        }
        try
        {
            var place = (await geo.SearchAsync(q, 1, "bd", ct)).FirstOrDefault();
            return place is null
                ? NotFoundError($"Could not find \"{q}\".")
                : Ok(new { success = true, data = ToJson(place) });
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Geocoding failed");
            return Error(StatusCodes.Status502BadGateway, "Geocoding is unavailable right now.");
        }
    }

    // Road distance in km, travel time in minutes, and an estimated fare.
    [HttpGet("distance")]
    public async Task<IActionResult> Distance([FromQuery] double sourceLat, [FromQuery] double sourceLon,
        [FromQuery] double destLat, [FromQuery] double destLon, CancellationToken ct = default)
    {
        if (!ValidPoint(sourceLat, sourceLon) || !ValidPoint(destLat, destLon))
        {
            return BadRequestError("Invalid coordinates.");
        }

        var road = await geo.RoadDistanceAsync(sourceLat, sourceLon, destLat, destLon, ct);
        return Ok(new
        {
            success = true,
            data = new
            {
                distance = road.DistanceKm,
                duration = road.DurationMinutes,
                fare = fares.FareFor(road.DistanceKm),
                farePerKm = fares.FarePerKm,
                estimated = road.Estimated,
            },
        });
    }

    private static bool ValidPoint(double lat, double lon) =>
        double.IsFinite(lat) && double.IsFinite(lon) && Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180;

    private static object ToJson(GeoPlace p) => new { display_name = p.DisplayName, lat = p.Lat, lon = p.Lon, place_id = p.PlaceId };
}

[Route("api/stops")]
public class StopsController(AppDbContext db, BusRouteCache routes) : ApiControllerBase
{
    // Every bus stop once, by name, with its coordinates. A stop served by several routes is stored once per route,
    // so its position is the average of those rows.
    [HttpGet("")]
    public async Task<IActionResult> All(CancellationToken ct)
    {
        var stops = await DistinctStopsAsync(ct);
        return Ok(new { success = true, count = stops.Count, data = stops.Select(s => new { name = s.Name, lat = s.Lat, lng = s.Lng }) });
    }

    // Bus stops closest to a point, nearest first, with the straight-line distance to each.
    [HttpGet("nearby")]
    public async Task<IActionResult> Nearby([FromQuery] double? lat, [FromQuery] double? lng,
        [FromQuery] double radiusKm = 1.5, [FromQuery] int limit = 5, CancellationToken ct = default)
    {
        if (lat is not { } la || lng is not { } ln || !double.IsFinite(la) || !double.IsFinite(ln) || Math.Abs(la) > 90 || Math.Abs(ln) > 180)
        {
            return BadRequestError("lat and lng are required and must be valid coordinates.");
        }

        var radius = Math.Clamp(radiusKm, 0.1, 10);
        var stops = (await DistinctStopsAsync(ct))
            .Select(s => new { name = s.Name, lat = s.Lat, lng = s.Lng, distanceKm = Math.Round(GeoService.HaversineKm(la, ln, s.Lat, s.Lng), 2) })
            .Where(s => s.distanceKm <= radius)
            .OrderBy(s => s.distanceKm)
            .Take(Math.Clamp(limit, 1, 20))
            .ToList();
        return Ok(new { success = true, count = stops.Count, data = stops });
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? q)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Ok(new { success = true, data = Array.Empty<object>() });
        }

        var term = q.Trim();
        var pattern = term.ContainsPattern();
        var stops = await db.BusStops
            .Where(s => EF.Functions.ILike(s.Name, pattern))
            .GroupBy(s => s.Name)
            .Select(g => new { name = g.Key, lat = g.Average(s => s.Lat), lng = g.Average(s => s.Lng) })
            .ToListAsync();

        var ordered = stops
            .OrderByDescending(s => s.name.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            .ThenBy(s => s.name)
            .Take(10);
        return Ok(new { success = true, data = ordered });
    }

    // Returns success=false (still 200) when nothing matches, so callers can fall back to geocoding.
    [HttpGet("coordinates")]
    public async Task<IActionResult> Coordinates([FromQuery] string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Ok(new { success = false, message = "name is required.", data = (object?)null });
        }

        var exact = name.EscapeLike();
        var contains = name.ContainsPattern();
        var stop = await db.BusStops.FirstOrDefaultAsync(s => EF.Functions.ILike(s.Name, exact))
                   ?? await db.BusStops.OrderBy(s => s.Name.Length).FirstOrDefaultAsync(s => EF.Functions.ILike(s.Name, contains));
        return stop is null
            ? Ok(new { success = false, message = "Stop not found.", data = (object?)null })
            : Ok(new { success = true, data = new { name = stop.Name, lat = stop.Lat, lng = stop.Lng } });
    }

    private async Task<List<(string Name, double Lat, double Lng)>> DistinctStopsAsync(CancellationToken ct) =>
        (await routes.AllAsync(ct))
            .SelectMany(r => r.Stops)
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.First().Name, Lat: g.Average(s => s.Lat), Lng: g.Average(s => s.Lng)))
            .OrderBy(s => s.Name)
            .ToList();
}

[Route("api/fares")]
public class FaresController(AppDbContext db, FareService fares) : ApiControllerBase
{
    private const string NotConfigured = "Fare calculation is unavailable: the fare rate (Rewards:FarePerKm) is not configured.";

    // The configured fare rate, so the calculator can show it before anything is calculated.
    [HttpGet("rate")]
    public IActionResult Rate() => Ok(new
    {
        success = true,
        data = new { farePerKm = fares.FarePerKm, currency = "BDT", configured = fares.IsConfigured },
    });

    // Road distance between two places times the configured fare rate. Every successful calculation is saved
    // (to the caller's history when signed in); an identical one sent again within a minute is not saved twice.
    [EnableRateLimiting("maps")]
    [HttpPost("calculate")]
    public async Task<IActionResult> Calculate(CalculateFareRequest request, CancellationToken ct)
    {
        if (!fares.IsConfigured)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, NotConfigured);
        }
        if (string.IsNullOrWhiteSpace(request.Source) || string.IsNullOrWhiteSpace(request.Destination))
        {
            return BadRequestError("Choose both a starting point and a destination.");
        }
        var from = request.SourceCoordinates!;
        var to = request.DestinationCoordinates!;
        if (GeoService.HaversineKm(from.Lat, from.Lon, to.Lat, to.Lon) < 0.05)
        {
            return BadRequestError("The starting point and destination are the same place.");
        }

        var userId = IsSignedIn ? CurrentUserId : (Guid?)null;
        var (record, road, created) = await fares.CalculateAndSaveAsync(userId, request, ct);
        var body = new
        {
            success = true,
            message = created ? "Fare calculated and saved." : "Fare calculated (already saved).",
            data = new
            {
                fare = FareViewModel.From(record),
                duration = road.DurationMinutes,
                path = road.Path,
                saved = created,
            },
        };
        return created ? Created201(body) : Ok(body);
    }

    // The signed-in user's saved fare calculations, newest first.
    [Authorize]
    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var history = await fares.HistoryAsync(CurrentUserId, limit, ct);
        return Ok(new { success = true, count = history.Count, data = history.Select(FareViewModel.From) });
    }

    [HttpPost("")]
    public async Task<IActionResult> Save(SaveFareRequest request)
    {
        if (request.UserId is { } userId)
        {
            if (!await db.Users.AnyAsync(u => u.Id == userId))
            {
                return NotFoundError("User not found.");
            }
            if (!CanActAs(userId))
            {
                return ForbiddenError();
            }
        }
        if (!fares.IsConfigured)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, NotConfigured);
        }

        var fare = new FareRecord
        {
            UserId = request.UserId,
            Source = request.Source.Trim(),
            Destination = request.Destination.Trim(),
            Distance = Math.Round(request.Distance, 2),
            FarePerKm = fares.FarePerKm,
            Fare = fares.FareFor(request.Distance),
            SourceLat = request.SourceCoordinates?.Lat,
            SourceLon = request.SourceCoordinates?.Lon,
            DestinationLat = request.DestinationCoordinates?.Lat,
            DestinationLon = request.DestinationCoordinates?.Lon,
        };
        db.Fares.Add(fare);
        await db.SaveChangesAsync();
        return Created201(new { success = true, message = "Fare saved.", data = FareViewModel.From(fare) });
    }

    // Most recent saved fare for a route, preferring the user's own if userId is given.
    [HttpGet("")]
    public async Task<IActionResult> Get([FromQuery] string? source, [FromQuery] string? destination, [FromQuery] Guid? userId)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
        {
            return BadRequestError("source and destination are required.");
        }

        var src = source.EscapeLike();
        var dst = destination.EscapeLike();
        var query = db.Fares.Where(f => EF.Functions.ILike(f.Source, src) && EF.Functions.ILike(f.Destination, dst));
        var fare = (userId is null ? null : await query.Where(f => f.UserId == userId).OrderByDescending(f => f.CreatedAt).FirstOrDefaultAsync())
                   ?? await query.OrderByDescending(f => f.CreatedAt).FirstOrDefaultAsync();

        return fare is null
            ? NotFoundError("Fare not found.")
            : Ok(new { success = true, data = FareViewModel.From(fare) });
    }

    // A signed-in user may only list their own fares (admins excepted).
    [HttpGet("user/{userId:guid}")]
    public async Task<IActionResult> ForUser(Guid userId)
    {
        if (!CanActAs(userId))
        {
            return ForbiddenError();
        }
        var records = await db.Fares
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Take(100)
            .AsNoTracking()
            .ToListAsync();
        return Ok(new { success = true, count = records.Count, data = records.Select(FareViewModel.From) });
    }
}
