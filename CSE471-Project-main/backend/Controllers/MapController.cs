using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Extensions;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

[Route("api/geocoding")]
public class GeocodingController(GeoService geo, WalletService wallets, ILogger<GeocodingController> logger) : ApiControllerBase
{
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] int limit = 5,
        [FromQuery] string countrycodes = "bd", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequestError("q is required.");
        }
        try
        {
            var places = await geo.SearchAsync(q, limit, countrycodes, ct);
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
        if (Math.Abs(sourceLat) > 90 || Math.Abs(destLat) > 90 || Math.Abs(sourceLon) > 180 || Math.Abs(destLon) > 180)
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
                fare = Math.Round((decimal)road.DistanceKm * wallets.Rewards.FarePerKm, 2),
                estimated = road.Estimated,
            },
        });
    }

    private static object ToJson(GeoPlace p) => new { display_name = p.DisplayName, lat = p.Lat, lon = p.Lon, place_id = p.PlaceId };
}

[Route("api/stops")]
public class StopsController(AppDbContext db) : ApiControllerBase
{
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
}

[Route("api/fares")]
public class FaresController(AppDbContext db, WalletService wallets) : ApiControllerBase
{
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

        var fare = new FareRecord
        {
            UserId = request.UserId,
            Source = request.Source.Trim(),
            Destination = request.Destination.Trim(),
            Distance = Math.Round(request.Distance, 2),
            Fare = Math.Round((decimal)request.Distance * wallets.Rewards.FarePerKm, 2),
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

    [HttpGet("user/{userId:guid}")]
    public async Task<IActionResult> ForUser(Guid userId)
    {
        var fares = await db.Fares
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Take(100)
            .AsNoTracking()
            .ToListAsync();
        return Ok(new { success = true, count = fares.Count, data = fares.Select(FareViewModel.From) });
    }
}
