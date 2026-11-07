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
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var bus = await buses.VehicleAsync(id);
        return bus is null
            ? NotFoundError("Bus not found.")
            : Ok(new { success = true, data = IndividualBusViewModel.From(bus) });
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
