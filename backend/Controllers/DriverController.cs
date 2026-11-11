using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCommute.Server.Extensions;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// F22: the verified driver's dashboard, schedule, trips, routes, working status and statistics. Every endpoint needs
// an approved driver (checked against the database by DriverAccessHandler) and only ever reads or changes the
// signed-in driver's own data: the driver id always comes from the token, never from the request.
[Authorize(Policy = DriverPolicies.Approved)]
[Route("api/driver")]
public class DriverController(DriverTripService trips, DriverRegistrationService registrations) : ApiControllerBase
{
    private static DateTime Now => DateTime.UtcNow;

    private static DateOnly? Date(string? text) => DriverRules.ParseDate(text);

    private async Task<Func<DriverTrip, DriverTripViewModel>> Viewer(bool withStops = false)
    {
        var active = await trips.ActiveBusIdsAsync(CurrentUserId);
        var now = Now;
        return t => DriverTripViewModel.From(t, trips.StateOf(t, active, now), withStops: withStops);
    }

    private IActionResult Page(List<DriverTrip> items, int total, int page, int pageSize, Func<DriverTrip, DriverTripViewModel> view) => Ok(new
    {
        success = true, page, pageSize, total, totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)), data = items.Select(view),
    });

    // ---- Dashboard ----

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var now = Now;
        var d = await trips.DashboardAsync(CurrentUserId, now);
        if (d is null)
        {
            return NotFoundError("No driver registration was found for this account.");
        }
        DriverTripViewModel View(DriverTrip t) => DriverTripViewModel.From(t, trips.StateOf(t, d.ActiveBusIds, now));
        var counted = d.Today.Where(t => t.Status != DriverTripStatus.Cancelled).ToList();
        return Ok(new
        {
            success = true,
            data = new
            {
                driver = new
                {
                    id = d.Profile.UserId, name = d.Name, email = d.Email, phone = d.Phone,
                    applicationCode = d.Profile.ApplicationCode, verificationStatus = d.Profile.VerificationStatus, hasPhoto = d.HasPhoto,
                    licenseType = d.Profile.LicenseType, experienceYears = d.Profile.DrivingExperienceYears,
                },
                today = DriverTripService.Today(now).ToString("yyyy-MM-dd"),
                workingStatus = WorkingStatusViewModel.From(d.Status, d.Active is not null),
                activeTrip = d.Active is null ? null : View(d.Active),
                assignments = d.Assignments.Select(a => new { assignment = DriverAssignmentViewModel.From(a), route = DriverRouteViewModel.From(a.Bus!) }),
                schedule = new
                {
                    total = counted.Count,
                    completed = counted.Count(t => t.Status == DriverTripStatus.Completed),
                    remaining = counted.Count(t => DriverTripStatus.Open.Contains(t.Status)),
                    missed = counted.Count(t => t.Status == DriverTripStatus.Missed),
                    firstDeparture = counted.FirstOrDefault()?.ScheduledDeparture,
                    lastDeparture = counted.LastOrDefault()?.ScheduledDeparture,
                    trips = d.Today.Select(View),
                },
                completed = new { total = d.Completed, thisWeek = d.CompletedWeek, thisMonth = d.CompletedMonth, completionRate = d.CompletionRate },
                upcoming = new { total = d.UpcomingTotal, trips = d.Upcoming.Select(View) },
                routes = d.Routes.Select(DriverRouteViewModel.From),
            },
        });
    }

    // ---- Assigned bus ----

    // The driver's assignments (active first) with the route and vehicle. Empty when no bus is assigned yet.
    [HttpGet("assigned-bus")]
    public async Task<IActionResult> AssignedBus()
    {
        var assignments = await trips.AssignmentsAsync(CurrentUserId);
        return Ok(new
        {
            success = true,
            data = assignments.Select(a => new
            {
                assignment = DriverAssignmentViewModel.From(a),
                route = DriverRouteViewModel.From(a.Bus!),
                vehicle = DriverVehicleViewModel.From(a.IndividualBus),
                // Kept for the F21 dashboard: the route's own fields at the top level.
                id = a.BusId, busName = a.Bus!.BusName, routeNumber = a.Bus.RouteNumber, @operator = a.Bus.Operator, busType = a.Bus.BusType,
                isActive = a.IsActive,
            }),
        });
    }

    // ---- Schedule (the same trips, by day) ----

    [HttpGet("schedule/today")]
    public async Task<IActionResult> ScheduleToday()
    {
        var today = DriverTripService.Today(Now);
        var (items, total) = await trips.TripsAsync(CurrentUserId, new DriverTripFilter(today, today, Oldest: true, PageSize: 100), Now);
        return Page(items, total, 1, 100, await Viewer());
    }

    // ?from&to (default: the next 7 days)
    [HttpGet("schedule/upcoming")]
    public async Task<IActionResult> ScheduleUpcoming([FromQuery] string? from, [FromQuery] string? to, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var today = DriverTripService.Today(Now);
        var start = Date(from) ?? today;
        var end = Date(to) ?? today.AddDays(7);
        return await List(new DriverTripFilter(start, end, UpcomingOnly: true, Oldest: true), page, pageSize);
    }

    // ?date or from/to, status
    [HttpGet("schedule/history")]
    public async Task<IActionResult> ScheduleHistory([FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var today = DriverTripService.Today(Now);
        return await List(new DriverTripFilter(Date(from), Date(to) ?? today.AddDays(-1), Status(status)), page, pageSize);
    }

    [HttpGet("schedule/{scheduleId:guid}")]
    public Task<IActionResult> Schedule(Guid scheduleId) => Trip(scheduleId);

    // ---- Trips ----

    // ?date=yyyy-MM-dd | from&to · status · search · sort=newest|oldest · page · pageSize
    [HttpGet("trips")]
    public async Task<IActionResult> Trips([FromQuery] string? date, [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? status,
        [FromQuery] string? search, [FromQuery] string? sort, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var day = Date(date);
        if (status is not null && Status(status) is null && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase) && status.Length > 0)
        {
            return BadRequestError("Unknown trip status.");
        }
        return await List(new DriverTripFilter(day ?? Date(from), day ?? Date(to), Status(status), Trim(search), string.Equals(sort, "oldest", StringComparison.OrdinalIgnoreCase)), page, pageSize);
    }

    [HttpGet("trips/history")]
    public async Task<IActionResult> TripHistory([FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? status, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 15) =>
        await List(new DriverTripFilter(Date(from), Date(to), Status(status), Trim(search), PastOnly: true), page, pageSize);

    // ?range=today|tomorrow|week or from/to
    [HttpGet("trips/upcoming")]
    public async Task<IActionResult> UpcomingTrips([FromQuery] string? range, [FromQuery] string? from, [FromQuery] string? to, [FromQuery] int page = 1, [FromQuery] int pageSize = 30)
    {
        var today = DriverTripService.Today(Now);
        var (start, end) = range?.ToLowerInvariant() switch
        {
            "today" => (today, today),
            "tomorrow" => (today.AddDays(1), today.AddDays(1)),
            "week" => (today, today.AddDays(7)),
            _ => (Date(from) ?? today, Date(to) ?? today.AddDays(60)),
        };
        if (end < start)
        {
            return BadRequestError("The end date must be on or after the start date.");
        }
        return await List(new DriverTripFilter(start, end, UpcomingOnly: true, Oldest: true), page, pageSize);
    }

    [HttpGet("trips/{tripId:guid}")]
    public async Task<IActionResult> Trip(Guid tripId)
    {
        var trip = await trips.TripAsync(CurrentUserId, tripId, Now);
        if (trip is null)
        {
            return NotFoundError("Trip not found.");
        }
        var active = await trips.ActiveBusIdsAsync(CurrentUserId);
        return Ok(new { success = true, data = DriverTripViewModel.From(trip, trips.StateOf(trip, active, Now), withDriver: true, withStops: true) });
    }

    [HttpPost("trips/{tripId:guid}/start")]
    public Task<IActionResult> Start(Guid tripId) => Act(trips.StartAsync(CurrentUserId, tripId));

    [HttpPost("trips/{tripId:guid}/complete")]
    public Task<IActionResult> Complete(Guid tripId) => Act(trips.CompleteAsync(CurrentUserId, tripId));

    private async Task<IActionResult> Act(Task<ServiceResult<DriverTrip>> action)
    {
        var result = await action;
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        var trip = (await trips.TripAsync(CurrentUserId, result.Value!.Id, Now))!;
        var active = await trips.ActiveBusIdsAsync(CurrentUserId);
        return Ok(new
        {
            success = true,
            message = result.Message,
            data = DriverTripViewModel.From(trip, trips.StateOf(trip, active, Now), withDriver: true, withStops: true),
            workingStatus = WorkingStatusViewModel.From(await trips.StatusAsync(CurrentUserId), trip.Status == DriverTripStatus.InProgress),
        });
    }

    private async Task<IActionResult> List(DriverTripFilter f, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (items, total) = await trips.TripsAsync(CurrentUserId, f with { Page = page, PageSize = pageSize }, Now);
        return Page(items, total, page, pageSize, await Viewer());
    }

    private static string? Status(string? status)
    {
        var s = status?.Trim().ToUpperInvariant().Replace(' ', '_');
        return s is not null && DriverTripStatus.All.Contains(s) ? s : null;
    }

    private static string? Trim(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim()[..Math.Min(text.Trim().Length, 100)];

    // ---- Routes ----

    [HttpGet("routes")]
    public async Task<IActionResult> Routes() => Ok(new { success = true, data = (await trips.RoutesAsync(CurrentUserId)).Select(DriverRouteViewModel.From) });

    // Only a route the driver is assigned to or scheduled on.
    [HttpGet("routes/{routeId:guid}")]
    public async Task<IActionResult> RouteDetails(Guid routeId) => await trips.RouteAsync(CurrentUserId, routeId) is { } route
        ? Ok(new { success = true, data = DriverRouteViewModel.From(route) })
        : NotFoundError("Route not found.");

    // ---- Working status ----

    [HttpGet("working-status")]
    public async Task<IActionResult> WorkingStatus()
    {
        var status = await trips.StatusAsync(CurrentUserId);
        var active = await trips.ActiveTripAsync(CurrentUserId);
        return Ok(new
        {
            success = true,
            data = WorkingStatusViewModel.From(status, active is not null),
            history = (await trips.StatusHistoryAsync(CurrentUserId)).Select(StatusHistoryViewModel.From),
        });
    }

    [HttpPut("working-status")]
    public async Task<IActionResult> SetWorkingStatus(WorkingStatusRequest request)
    {
        var result = await trips.SetStatusAsync(CurrentUserId, request.Status, request.Reason);
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, data = WorkingStatusViewModel.From(result.Value!, false),
                history = (await trips.StatusHistoryAsync(CurrentUserId)).Select(StatusHistoryViewModel.From) })
            : Error(result.StatusCode, result.Message);
    }

    // ---- Statistics ----

    [HttpGet("statistics")]
    public async Task<IActionResult> Statistics() => Ok(new { success = true, data = await trips.StatsAsync(CurrentUserId, Now) });

    [HttpGet("statistics/weekly")]
    public async Task<IActionResult> Weekly()
    {
        var s = await trips.StatsAsync(CurrentUserId, Now);
        return Ok(new { success = true, data = new { daily = s.Daily, weeks = s.Weekly } });
    }

    [HttpGet("statistics/monthly")]
    public async Task<IActionResult> Monthly() => Ok(new { success = true, data = (await trips.StatsAsync(CurrentUserId, Now)).Monthly });
}

// F22 admin: duty schedules, bus assignments and the driver operations overview. Admins only.
[Authorize(Roles = UserRoles.Admin)]
[Route("api/admin")]
public class AdminDriverScheduleController(DriverScheduleService schedules, DriverVerificationService verification, DriverTripService trips) : ApiControllerBase
{
    private static DateOnly? Date(string? text) => DriverRules.ParseDate(text);

    // Approved drivers with working status, buses, today's progress and missed trips (last 30 days).
    [HttpGet("driver-operations")]
    public async Task<IActionResult> Operations()
    {
        var rows = await schedules.OperationsAsync(DateTime.UtcNow);
        return Ok(new
        {
            success = true,
            data = rows.Select(r => new
            {
                driverId = r.Driver.Id, name = r.Driver.Name, email = r.Driver.Email, phone = r.Driver.Phone ?? "", applicationCode = r.Profile.ApplicationCode,
                workingStatus = r.WorkingStatus, statusChangedAt = r.StatusChangedAt,
                assignments = r.Assignments.Select(DriverAssignmentViewModel.From),
                todayTotal = r.TodayTotal, todayCompleted = r.TodayCompleted, missedLast30Days = r.MissedLast30Days,
                activeTrip = r.ActiveTrip is null ? null : DriverTripViewModel.From(r.ActiveTrip),
                nextTrip = r.NextTrip is null ? null : DriverTripViewModel.From(r.NextTrip),
            }),
        });
    }

    [HttpGet("driver-operations/{driverId:guid}/status-history")]
    public async Task<IActionResult> StatusHistory(Guid driverId) => Ok(new
    {
        success = true,
        data = WorkingStatusViewModel.From(await trips.StatusAsync(driverId), await trips.ActiveTripAsync(driverId) is not null),
        history = (await trips.StatusHistoryAsync(driverId, 30)).Select(StatusHistoryViewModel.From),
    });

    // ?driverId · busId · from/to · status · page · pageSize
    [HttpGet("driver-schedules")]
    public async Task<IActionResult> List([FromQuery] Guid? driverId, [FromQuery] Guid? busId, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        var s = string.IsNullOrWhiteSpace(status) || status.Equals("all", StringComparison.OrdinalIgnoreCase) ? null : status.Trim().ToUpperInvariant();
        if (s is not null && !DriverTripStatus.All.Contains(s))
        {
            return BadRequestError("Unknown trip status.");
        }
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (items, total) = await schedules.ListAsync(new AdminScheduleFilter(driverId, busId, Date(from), Date(to), s, page, pageSize), DateTime.UtcNow);
        return Ok(new
        {
            success = true, page, pageSize, total, totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)),
            data = items.Select(t => DriverTripViewModel.From(t, withDriver: true)),
        });
    }

    [HttpGet("driver-schedules/{id:guid}")]
    public async Task<IActionResult> Get(Guid id) => await schedules.FindAsync(id) is { } trip
        ? Ok(new { success = true, data = DriverTripViewModel.From(trip, withDriver: true, withStops: true) })
        : NotFoundError("Schedule not found.");

    [HttpPost("driver-schedules")]
    public async Task<IActionResult> Create(DriverScheduleRequest request) => Result(await schedules.CreateAsync(CurrentUserId, request));

    [HttpPut("driver-schedules/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, DriverScheduleRequest request) => Result(await schedules.UpdateAsync(CurrentUserId, id, request));

    // Cancels the trip (kept for the record). Body: { "reason": "…" }
    [HttpDelete("driver-schedules/{id:guid}")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] ReasonRequest? request) => Result(await schedules.CancelAsync(CurrentUserId, id, request?.Reason));

    [HttpPost("driver-schedules/{id:guid}/mark-missed")]
    public async Task<IActionResult> MarkMissed(Guid id, ReasonRequest request) => Result(await schedules.MarkMissedAsync(CurrentUserId, id, request.Reason));

    private IActionResult Result(ServiceResult<DriverTrip> result) => result.Succeeded
        ? StatusCode(result.StatusCode, new { success = true, message = result.Message, data = DriverTripViewModel.From(result.Value!, withDriver: true, withStops: true) })
        : Error(result.StatusCode, result.Message);

    // The vehicles (IndividualBus rows) on one route, for choosing a driver's vehicle.
    [HttpGet("buses/{busId:guid}/vehicles")]
    public async Task<IActionResult> Vehicles(Guid busId, [FromServices] Data.AppDbContext db) => Ok(new
    {
        success = true,
        data = (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            db.IndividualBuses.Where(v => v.BusId == busId).OrderBy(v => v.BusCode))).Select(DriverVehicleViewModel.From),
    });

    // ---- Bus assignments (the same rows as F21's assign-bus) ----

    [HttpPost("driver-assignments")]
    public async Task<IActionResult> Assign(AdminAssignmentRequest request)
    {
        if (request.DriverId is not { } driverId || request.BusId is not { } busId)
        {
            return BadRequestError("Choose the driver and the bus.");
        }
        return Assignment(await verification.AssignBusAsync(CurrentUserId, driverId, busId, request.IndividualBusId));
    }

    // Body: { "individualBusId": "…" | null }: the vehicle on the assigned route.
    [HttpPut("driver-assignments/{id:guid}")]
    public async Task<IActionResult> UpdateAssignment(Guid id, AdminAssignmentRequest request) =>
        Assignment(await verification.SetAssignmentVehicleAsync(id, request.IndividualBusId));

    // Ends the assignment and cancels the driver's future trips on that bus.
    [HttpDelete("driver-assignments/{id:guid}")]
    public async Task<IActionResult> EndAssignment(Guid id)
    {
        if (await verification.AssignmentDriverAsync(id) is not { } driverId)
        {
            return NotFoundError("Assignment not found.");
        }
        return Assignment(await verification.EndAssignmentAsync(CurrentUserId, driverId, id));
    }

    private IActionResult Assignment(ServiceResult<DriverBusAssignment> result) => result.Succeeded
        ? StatusCode(result.StatusCode, new { success = true, message = result.Message, data = DriverAssignmentViewModel.From(result.Value!) })
        : Error(result.StatusCode, result.Message);
}
