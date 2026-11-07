using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// The signed-in rider's trip history. The rules live in TripService.
[Authorize]
[Route("trip")]
public class TripController(TripService trips, BusRouteCache routes) : ApiControllerBase
{
    // Logs a finished trip straight into history (no boarding, no gems). Gems are only earned by boarding a bus and
    // ending the trip, so a client cannot claim rewards for trips it made up.
    [HttpPost("add")]
    public Task<IActionResult> Add(AddTripRequest request) => Log(request);

    // Older route that used to award gems for any trip a client sent; it now logs like /trip/add.
    [HttpPost("")]
    public Task<IActionResult> Complete(AddTripRequest request) => Log(request);

    // The rider's trip on the bus right now, with the route's stops so the client can offer where to get off.
    [HttpGet("active")]
    public async Task<IActionResult> Active()
    {
        var trip = await trips.ActiveTripAsync(CurrentUserId);
        if (trip is null)
        {
            return Ok(new { success = true, data = (object?)null });
        }
        var route = Guid.TryParse(trip.BusId, out var routeId) ? await routes.ByIdAsync(routeId) : null;
        return Ok(new { success = true, data = TripViewModel.From(trip), route });
    }

    [HttpGet("user")]
    public Task<IActionResult> UserTrips() => ListTrips();

    [HttpGet("")]
    public Task<IActionResult> List() => ListTrips();

    // Only the signed-in rider's own trips, newest first; there is no user id to change in the request.
    private async Task<IActionResult> ListTrips()
    {
        var history = await trips.HistoryAsync(CurrentUserId);
        return Ok(new { success = true, count = history.Count, data = history.Select(TripViewModel.From) });
    }

    private async Task<IActionResult> Log(AddTripRequest request)
    {
        var result = await trips.LogAsync(CurrentUserId, request);
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        var (trip, created) = result.Value;
        var body = new { success = true, message = result.Message, data = TripViewModel.From(trip) };
        return created ? Created201(body) : Ok(body);
    }
}
