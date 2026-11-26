using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartCommute.Server.Extensions;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// F23: live bus locations. Drivers (approved, with a trip in progress on their assigned bus) start, update and stop
// sharing; the driver is always the user in the token, so nobody can move another driver's bus. Signed-in users
// read the active buses, one bus, and its route progress. Real-time delivery is by polling: the active list is
// served from a short shared cache, so many commuters watching the same bus cost one database read every few
// seconds.
[Route("api/live-location")]
public class LiveLocationController(LiveLocationService live) : ApiControllerBase
{
    private static DateTime Now => DateTime.UtcNow;

    // ---- Driver ----

    [Authorize(Policy = DriverPolicies.Approved)]
    [HttpGet("me")]
    public async Task<IActionResult> Me() =>
        Ok(new { success = true, data = DriverLiveStateViewModel.From(await live.StateAsync(CurrentUserId), Now, live.Settings) });

    [Authorize(Policy = DriverPolicies.Approved)]
    [EnableRateLimiting("strict")]
    [HttpPost("start")]
    public async Task<IActionResult> Start()
    {
        var result = await live.StartAsync(CurrentUserId);
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, data = DriverLiveStateViewModel.From(await live.StateAsync(CurrentUserId), Now, live.Settings) })
            : Error(result.StatusCode, result.Message);
    }

    // Body: { "latitude": 23.8103, "longitude": 90.4125, "accuracy": 15, "speed": 25, "heading": 180 }
    [Authorize(Policy = DriverPolicies.Approved)]
    [EnableRateLimiting("live-location")]
    [HttpPost("update")]
    public async Task<IActionResult> Update(LiveLocationUpdateRequest request)
    {
        var result = await live.UpdateAsync(CurrentUserId, new LiveLocationUpdate(request.Latitude, request.Longitude, request.Accuracy, request.Speed, request.Heading));
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        var (row, throttled) = result.Value;
        return Ok(new { success = true, message = result.Message, throttled, lastUpdatedAt = row.LastUpdatedAt, freshness = RouteProgress.Freshness(row.LastUpdatedAt, Now, live.Settings) });
    }

    // Also used when the driver closes the page (sent with fetch keepalive).
    [Authorize(Policy = DriverPolicies.Approved)]
    [HttpPost("stop")]
    public async Task<IActionResult> Stop()
    {
        var result = await live.StopAsync(CurrentUserId);
        return Ok(new { success = true, message = result.Message, data = DriverLiveStateViewModel.From(await live.StateAsync(CurrentUserId), Now, live.Settings) });
    }

    // ---- Commuters (any signed-in user) ----

    // Buses with a recent location. ?search= matches bus name, route number or registration number.
    [Authorize]
    [HttpGet("active")]
    public async Task<IActionResult> Active([FromQuery] string? search)
    {
        var now = Now;
        var list = await live.ActiveAsync(now);
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            list = list.Where(b => b.BusName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (b.RouteNumber?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (b.RegistrationNumber?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }
        return Ok(new
        {
            success = true, count = list.Count, serverTime = now,
            settings = new { liveSeconds = live.Settings.LiveSeconds, delayedSeconds = live.Settings.DelayedSeconds },
            data = list.Select(b => LiveBusViewModel.From(b, now, live.Settings)),
        });
    }

    // {busId}: a route (bus) id or a vehicle id. ?session= picks one bus when several on a route are sharing.
    [Authorize]
    [HttpGet("bus/{busId:guid}")]
    public async Task<IActionResult> Bus(Guid busId, [FromQuery] Guid? session)
    {
        var now = Now;
        var found = await live.FindAsync(busId, session);
        if (found is not null)
        {
            return Ok(new { success = true, serverTime = now, data = LiveBusViewModel.From(found, now, live.Settings) });
        }
        var info = await live.BusInfoAsync(busId);
        return info is { } i
            ? Ok(new { success = true, serverTime = now, data = LiveBusViewModel.NotSharing(i.BusId, i.BusName, i.RouteNumber, i.Operator, i.BusType, i.Registration) })
            : NotFoundError("Bus not found.");
    }

    // The bus with its route (stops in travel order) and how far along it is. Progress is estimated from the
    // route's stops: the bus is projected onto the straight lines between consecutive stops.
    [Authorize]
    [HttpGet("bus/{busId:guid}/progress")]
    public async Task<IActionResult> Progress(Guid busId, [FromQuery] Guid? session)
    {
        var now = Now;
        var found = await live.FindAsync(busId, session);
        LiveBusViewModel bus;
        Guid routeId;
        string direction;
        if (found is not null)
        {
            bus = LiveBusViewModel.From(found, now, live.Settings);
            routeId = found.BusId;
            direction = found.Direction;
        }
        else if (await live.BusInfoAsync(busId) is { } i)
        {
            bus = LiveBusViewModel.NotSharing(i.BusId, i.BusName, i.RouteNumber, i.Operator, i.BusType, i.Registration);
            routeId = i.BusId;
            direction = "FORWARD";
        }
        else
        {
            return NotFoundError("Bus not found.");
        }
        var result = await live.ProgressAsync(routeId, direction, bus.IsSharing ? bus.Latitude : null, bus.IsSharing ? bus.Longitude : null);
        if (result is not { } r)
        {
            return NotFoundError("Route not found.");
        }
        var p = r.Progress;
        return Ok(new
        {
            success = true,
            serverTime = now,
            data = new
            {
                bus,
                route = new
                {
                    id = routeId, name = r.Route.BusName, routeNumber = r.Route.RouteNumber, direction,
                    start = r.Stops.FirstOrDefault()?.Name, destination = r.Stops.LastOrDefault()?.Name,
                    stops = r.Stops.Select((s, index) => new { order = index, name = s.Name, lat = s.Lat, lng = s.Lng }),
                },
                progress = p is null ? null : new
                {
                    estimated = true,
                    method = "Projected onto straight lines between the route's stops",
                    percent = p.Percent, remainingPercent = Math.Round(100 - p.Percent, 1),
                    totalKm = p.TotalKm, travelledKm = p.TravelledKm, remainingKm = p.RemainingKm,
                    onRoute = p.OnRoute, offRouteMeters = p.OffRouteMeters,
                    currentStop = ProgressStopViewModel.From(p.CurrentStop), lastPassedStop = ProgressStopViewModel.From(p.LastPassedStop),
                    nextStop = ProgressStopViewModel.From(p.NextStop),
                    remainingStops = p.RemainingStops.Select(ProgressStopViewModel.From), remainingStopCount = p.RemainingStops.Count,
                },
            },
        });
    }
}
