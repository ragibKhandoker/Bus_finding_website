using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

[Authorize]
[Route("trip")]
public class TripController(AppDbContext db) : ApiControllerBase
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
    public async Task<IActionResult> Active([FromServices] TripService trips, [FromServices] BusRouteCache routes)
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
        var trips = await db.Trips
            .Include(t => t.IndividualBus)
            .Where(t => t.UserId == CurrentUserId)
            .OrderByDescending(t => t.CreatedAt)
            .AsNoTracking()
            .ToListAsync();
        return Ok(new { success = true, count = trips.Count, data = trips.Select(TripViewModel.From) });
    }

    private async Task<IActionResult> Log(AddTripRequest request)
    {
        var busId = request.BusId.Trim();
        var source = request.Source?.Trim() ?? "";
        var destination = request.Destination?.Trim() ?? "";
        if (busId.Length == 0)
        {
            return BadRequestError("busId is required.");
        }
        if (source.Length == 0 || destination.Length == 0)
        {
            return BadRequestError("Enter where the trip started and ended.");
        }
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequestError("The start and end of a trip cannot be the same place.");
        }

        var userId = CurrentUserId;
        if (await db.Trips.AnyAsync(t => t.UserId == userId && t.Status == TripStatus.Active && t.BusId == busId))
        {
            return Error(StatusCodes.Status409Conflict, "You are on this bus right now. End the trip instead of logging it.");
        }
        // The same trip sent twice in a row (a double click or a retry) is saved once.
        var since = DateTime.UtcNow.AddMinutes(-2);
        var duplicate = await db.Trips.FirstOrDefaultAsync(t => t.UserId == userId && t.BusId == busId && t.Source == source &&
                                                              t.Destination == destination && t.CreatedAt > since);
        if (duplicate is not null)
        {
            return Ok(new { success = true, message = "Trip already added.", data = TripViewModel.From(duplicate) });
        }

        var trip = new Trip
        {
            UserId = userId,
            BusId = busId,
            BusName = string.IsNullOrWhiteSpace(request.BusName) ? "Bus" : request.BusName.Trim(),
            Distance = request.Distance,
            Fare = request.Fare,
            Source = source,
            Destination = destination,
            Status = TripStatus.Completed,
            EndedAt = DateTime.UtcNow,
        };
        db.Trips.Add(trip);
        await db.SaveChangesAsync();
        return Created201(new { success = true, message = "Trip added.", data = TripViewModel.From(trip) });
    }
}
