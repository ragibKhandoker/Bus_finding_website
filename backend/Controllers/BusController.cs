using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// Bus routes: listing, searching by name or trip, and road estimates. The rules live in BusService.
[Authorize]
[Route("bus")]
public class BusController(BusService buses) : ApiControllerBase
{
    // Every route with its stops in order, served from memory. Public route information, shown to visitors too.
    [AllowAnonymous]
    [HttpGet("all")]
    public async Task<IActionResult> All()
    {
        var all = await buses.AllAsync(HttpContext.RequestAborted);
        return Ok(new { success = true, count = all.Count, data = all });
    }

    // Which routes have an uploaded photo: [{ busId, url, credit, updatedAt }]. Public, like the routes themselves.
    [AllowAnonymous]
    [HttpGet("photos")]
    public async Task<IActionResult> Photos([FromServices] BusPhotoService photos) =>
        Ok(new { success = true, data = await photos.AllAsync(HttpContext.RequestAborted) });

    // The uploaded photo of one route. Each version has its own URL (?v=...), so it may be cached for a long time.
    [AllowAnonymous]
    [HttpGet("{busId:guid}/photo")]
    public async Task<IActionResult> Photo(Guid busId, [FromServices] BusPhotoService photos)
    {
        var stored = await photos.OpenAsync(busId, HttpContext.RequestAborted);
        if (stored is null)
        {
            return NotFoundError("This bus has no photo.");
        }
        Response.Headers.CacheControl = "public, max-age=604800";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        // The web app runs on another origin in development, so the image must be allowed to load there.
        Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
        return File(stored.Content, stored.ContentType);
    }

    // One route with its stops, for the route's own page. Route numbers are matched without regard to case.
    [AllowAnonymous]
    [HttpGet("route/{routeNumber}")]
    public async Task<IActionResult> ByRouteNumber(string routeNumber)
    {
        var bus = await buses.ByRouteNumberAsync(routeNumber, HttpContext.RequestAborted);
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
            var (routes, stops) = await buses.CountAsync(HttpContext.RequestAborted);
            return Ok(new { success = true, routes, stops });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "Bus data is unavailable right now.");
        }
    }

    [HttpGet("search-by-name")]
    public async Task<IActionResult> SearchByName([FromQuery] string? busName)
    {
        var result = await buses.SearchByNameAsync(busName, HttpContext.RequestAborted);
        return result.Succeeded
            ? Ok(new { success = true, count = result.Value!.Count, data = result.Value })
            : Error(result.StatusCode, result.Message);
    }

    // start and end are the stops the typed places resolved to, for the map.
    [HttpGet("search-by-route")]
    public async Task<IActionResult> SearchByRoute([FromQuery] string? startLocation, [FromQuery] string? endLocation)
    {
        var result = await buses.SearchByRouteAsync(startLocation, endLocation, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        var search = result.Value!;
        return Ok(new { success = true, count = search.Buses.Count, data = search.Buses, start = search.Start, end = search.End });
    }

    // Buses between two points on the map, e.g. the places picked on the fare calculator.
    // ?fromLat=&fromLng=&toLat=&toLng=&walkKm=1 (how far the rider will walk to and from a stop, 0.2–3 km).
    [HttpGet("near-route")]
    public async Task<IActionResult> NearRoute([FromQuery] double? fromLat, [FromQuery] double? fromLng, [FromQuery] double? toLat,
        [FromQuery] double? toLng, [FromQuery] double walkKm = BusService.DefaultWalkKm)
    {
        if (!ValidPoint(fromLat, fromLng) || !ValidPoint(toLat, toLng))
        {
            return BadRequestError("fromLat, fromLng, toLat and toLng are required and must be valid coordinates.");
        }
        var result = await buses.SearchNearAsync(fromLat!.Value, fromLng!.Value, toLat!.Value, toLng!.Value, walkKm, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        return Ok(new
        {
            success = true,
            count = result.Value!.Count,
            data = result.Value.Select(o => new
            {
                bus = o.Bus,
                walkToBoardingKm = o.WalkToBoardingKm,
                walkFromAlightingKm = o.WalkFromAlightingKm,
                rideKm = o.RideKm,
                fare = o.Fare,
            }),
        });
    }

    private static bool ValidPoint(double? lat, double? lng) =>
        lat is { } la && lng is { } ln && double.IsFinite(la) && double.IsFinite(ln) && Math.Abs(la) <= 90 && Math.Abs(ln) <= 180;

    [EnableRateLimiting("maps")]
    [HttpGet("route-estimate")]
    public async Task<IActionResult> RouteEstimate([FromQuery] string? startLocation, [FromQuery] string? endLocation)
    {
        var result = await buses.RouteEstimateAsync(startLocation, endLocation, HttpContext.RequestAborted);
        return result.Succeeded ? Ok(new { success = true, data = result.Value }) : Error(result.StatusCode, result.Message);
    }

    // A route with its details and ordered stops, and the physical vehicles running on it.
    [HttpGet("individual/{id}")]
    public async Task<IActionResult> Individual(string id)
    {
        var result = await buses.RouteWithVehiclesAsync(id, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        var (bus, vehicles) = result.Value;
        return Ok(new { success = true, bus, data = vehicles.Select(IndividualBusViewModel.From) });
    }
}

[Authorize]
[Route("individual-bus")]
public class IndividualBusController(BusService buses) : ApiControllerBase
{
    // Vehicle codes matching the text, for suggestions while typing: ?q=AL-2
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] int limit = 8)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Ok(new { success = true, data = Array.Empty<object>() });
        }
        var found = await buses.SearchVehiclesAsync(q, limit);
        return Ok(new { success = true, data = found.Select(VehicleJson) });
    }

    // One vehicle by its code ("AL-2-01", what riders see on the bus) or its id.
    [HttpGet("{idOrCode}")]
    public async Task<IActionResult> Get(string idOrCode)
    {
        var bus = await buses.VehicleByIdOrCodeAsync(idOrCode);
        return bus is null
            ? NotFoundError($"No bus has the code \"{idOrCode.Trim()}\". Check the code written on the bus, e.g. AL-2-01.")
            : Ok(new { success = true, data = VehicleJson(bus) });
    }

    // The vehicle with its route's name and number.
    private static object VehicleJson(SmartCommute.Server.Models.IndividualBus b)
    {
        var v = IndividualBusViewModel.From(b);
        return new
        {
            _id = v.Id, v.ParentBusInfoId, v.BusCode, v.TotalPassengerCapacity, v.CurrentPassengerCount, v.Status, v.BusType,
            busName = b.Bus?.BusName, routeNumber = b.Bus?.RouteNumber,
        };
    }

    // Starts a trip on this vehicle for the signed-in rider (the user comes from the token, never the request).
    [HttpPost("{id:guid}/board")]
    public async Task<IActionResult> Board(Guid id, BoardRequest? request, [FromServices] TripService trips) =>
        Answer(await trips.BoardAsync(CurrentUserId, id, request?.BoardingStop, request?.DestinationStop));

    // Ends the rider's active trip on this vehicle: saves the fare and credits the gems, once.
    [HttpPost("{id:guid}/end-trip")]
    public async Task<IActionResult> EndTrip(Guid id, EndTripRequest? request, [FromServices] TripService trips) =>
        Answer(await trips.EndAsync(CurrentUserId, id, request?.Destination, request?.Source));

    // Older routes with the vehicle id in the body, kept for existing clients.
    [HttpPost("board")]
    public Task<IActionResult> BoardByBody(BoardRequest request, [FromServices] TripService trips) =>
        request.BusId is { } id ? Board(id, request, trips) : Task.FromResult<IActionResult>(BadRequestError("busId is required."));

    [HttpPost("end-trip")]
    public Task<IActionResult> EndTripByBody(EndTripRequest request, [FromServices] TripService trips) =>
        request.BusId is { } id ? EndTrip(id, request, trips) : Task.FromResult<IActionResult>(BadRequestError("busId is required."));

    private IActionResult Answer(TripResult result)
    {
        if (!result.Ok)
        {
            return Error(result.Status, result.Message);
        }
        object body = result.Wallet is null
            ? new { success = true, message = result.Message, data = TripViewModel.From(result.Trip!) }
            : new
            {
                success = true,
                message = result.Message,
                data = TripViewModel.From(result.Trip!),
                gemsEarned = result.Trip!.GemsEarned,
                wallet = WalletViewModel.From(result.Wallet),
            };
        return StatusCode(result.Status, body);
    }
}
