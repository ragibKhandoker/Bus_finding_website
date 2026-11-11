using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

public record AdminScheduleFilter(Guid? DriverId = null, Guid? BusId = null, DateOnly? From = null, DateOnly? To = null, string? Status = null,
    int Page = 1, int PageSize = 25);

public record DriverOperationsRow(User Driver, DriverProfile Profile, string WorkingStatus, DateTime? StatusChangedAt,
    List<DriverBusAssignment> Assignments, int TodayTotal, int TodayCompleted, int MissedLast30Days, DriverTrip? ActiveTrip, DriverTrip? NextTrip);

// F22, the admin's side: duty schedules (each a DriverTrip) and the operations overview. Only admins call this.
// Before a trip is scheduled the driver must be approved, the bus assigned to them and active, the route a Dhaka
// route in the database, and neither the driver nor the vehicle may already be busy at that time. Scheduling writes
// take one PostgreSQL advisory lock, so two admins cannot book the same driver for the same time at once.
public class DriverScheduleService(AppDbContext db, DriverTripService trips, NotificationService notifications)
{
    private const long ScheduleLock = 220022;
    public const int MaxTripMinutes = 6 * 60;

    // "2026-11-12" + "08:30" in Dhaka time → UTC.
    public static DateTime? DhakaToUtc(DateOnly date, string? time) =>
        TimeOnly.TryParseExact(time?.Trim(), ["HH:mm", "H:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock)
            ? DhakaTime.ToUtc(date.ToDateTime(clock))
            : null;

    public static (string Start, string End) Ends(Bus bus, string direction)
    {
        var stops = bus.Stops.OrderBy(s => s.Order).Select(s => s.Name).ToList();
        if (stops.Count == 0)
        {
            return ("", "");
        }
        return direction == DriverTripDirection.Return ? (stops[^1], stops[0]) : (stops[0], stops[^1]);
    }

    // ---- Reading ----

    public async Task<(List<DriverTrip> Items, int Total)> ListAsync(AdminScheduleFilter f, DateTime now)
    {
        await trips.SweepIfDueAsync(now);
        var query = db.DriverTrips.AsNoTracking().AsQueryable();
        if (f.DriverId is { } driverId) query = query.Where(t => t.DriverId == driverId);
        if (f.BusId is { } busId) query = query.Where(t => t.BusId == busId);
        if (f.From is { } from) query = query.Where(t => t.ScheduleDate >= from);
        if (f.To is { } to) query = query.Where(t => t.ScheduleDate <= to);
        if (f.Status is { Length: > 0 } status) query = query.Where(t => t.Status == status);
        var total = await query.CountAsync();
        var items = await query.OrderBy(t => t.ScheduledDeparture).ThenBy(t => t.Id).Include(t => t.Bus).Include(t => t.IndividualBus).Include(t => t.Driver)
            .Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).ToListAsync();
        return (items, total);
    }

    public Task<DriverTrip?> FindAsync(Guid id) => db.DriverTrips.Include(t => t.Bus!).ThenInclude(b => b.Stops).Include(t => t.IndividualBus)
        .Include(t => t.Driver).AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);

    // Every approved driver with their working status, buses and today's trips.
    public async Task<List<DriverOperationsRow>> OperationsAsync(DateTime now)
    {
        await trips.SweepIfDueAsync(now);
        var today = DriverTripService.Today(now);
        var since = today.AddDays(-30);
        var profiles = await db.DriverProfiles.Include(p => p.User).AsNoTracking()
            .Where(p => p.VerificationStatus == DriverVerificationStatus.Approved).OrderBy(p => p.User!.Name).ToListAsync();
        var ids = profiles.Select(p => p.UserId).ToList();
        var statuses = await db.DriverWorkingStatuses.AsNoTracking().Where(s => ids.Contains(s.DriverId)).ToDictionaryAsync(s => s.DriverId);
        var assignments = await db.DriverBusAssignments.Include(a => a.Bus).Include(a => a.IndividualBus).AsNoTracking()
            .Where(a => ids.Contains(a.DriverId) && a.IsActive).ToListAsync();
        var recent = await db.DriverTrips.Include(t => t.Bus).AsNoTracking()
            .Where(t => ids.Contains(t.DriverId) && (t.ScheduleDate >= since || t.Status == DriverTripStatus.Scheduled || t.Status == DriverTripStatus.InProgress)).ToListAsync();
        return profiles.Select(p =>
        {
            var mine = recent.Where(t => t.DriverId == p.UserId).ToList();
            var status = statuses.GetValueOrDefault(p.UserId);
            return new DriverOperationsRow(p.User!, p, status?.CurrentStatus ?? DriverWorkStatus.OffDuty, status?.LastStatusChangedAt,
                assignments.Where(a => a.DriverId == p.UserId).ToList(),
                mine.Count(t => t.ScheduleDate == today && t.Status != DriverTripStatus.Cancelled),
                mine.Count(t => t.ScheduleDate == today && t.Status == DriverTripStatus.Completed),
                mine.Count(t => t.Status == DriverTripStatus.Missed && t.ScheduleDate >= since),
                mine.FirstOrDefault(t => t.Status == DriverTripStatus.InProgress),
                mine.Where(t => t.Status == DriverTripStatus.Scheduled && t.ScheduledDeparture >= now).MinBy(t => t.ScheduledDeparture));
        }).ToList();
    }

    // ---- Creating and changing ----

    private record Plan(DriverBusAssignment Assignment, Bus Bus, IndividualBus? Vehicle, DateOnly Date, DateTime Departure, DateTime Arrival, string Direction);

    private async Task<(Plan? Plan, ServiceResult<DriverTrip>? Problem)> CheckAsync(DriverScheduleRequest r, DateTime now, Guid? editing)
    {
        static (Plan?, ServiceResult<DriverTrip>?) Fail(ServiceResult<DriverTrip> problem) => (null, problem);
        if (r.DriverId is not { } driverId || r.BusId is not { } busId)
        {
            return Fail(ServiceResult<DriverTrip>.BadRequest("Choose the driver and the bus."));
        }
        if (DriverRules.ParseDate(r.Date) is not { } date)
        {
            return Fail(ServiceResult<DriverTrip>.BadRequest("Enter the schedule date like 2026-11-12."));
        }
        if (DhakaToUtc(date, r.DepartureTime) is not { } departure || DhakaToUtc(date, r.ArrivalTime) is not { } arrival)
        {
            return Fail(ServiceResult<DriverTrip>.BadRequest("Enter the departure and expected arrival times like 08:30."));
        }
        if (arrival <= departure)
        {
            return Fail(ServiceResult<DriverTrip>.BadRequest("The expected arrival must be after the departure."));
        }
        if ((arrival - departure).TotalMinutes > MaxTripMinutes)
        {
            return Fail(ServiceResult<DriverTrip>.BadRequest("A trip can be at most 6 hours long."));
        }
        if (departure <= now)
        {
            return Fail(ServiceResult<DriverTrip>.BadRequest("The departure must be in the future."));
        }
        var direction = string.IsNullOrWhiteSpace(r.Direction) ? DriverTripDirection.Forward : r.Direction.Trim().ToUpperInvariant();
        if (!DriverTripDirection.All.Contains(direction))
        {
            return Fail(ServiceResult<DriverTrip>.BadRequest("Direction must be FORWARD or RETURN."));
        }

        var driver = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == driverId && u.Role == UserRoles.Driver);
        var profile = driver is null ? null : await db.DriverProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == driverId);
        if (profile is null)
        {
            return Fail(ServiceResult<DriverTrip>.NotFound("Driver not found."));
        }
        if (profile.VerificationStatus != DriverVerificationStatus.Approved || profile.AccountStatus != DriverAccountStatus.Active)
        {
            return Fail(ServiceResult<DriverTrip>.Conflict("Only approved, active drivers can be scheduled."));
        }
        var bus = await db.Buses.Include(b => b.Stops).AsNoTracking().FirstOrDefaultAsync(b => b.Id == busId);
        if (bus is null || bus.Stops.Count < 2)
        {
            return Fail(ServiceResult<DriverTrip>.NotFound("That route was not found in the Dhaka bus route database."));
        }
        if (direction == DriverTripDirection.Return && !bus.RunsBothWays)
        {
            return Fail(ServiceResult<DriverTrip>.BadRequest("This route runs one way only."));
        }
        var assignment = await db.DriverBusAssignments.AsNoTracking().FirstOrDefaultAsync(a => a.DriverId == driverId && a.BusId == busId && a.IsActive);
        if (assignment is null)
        {
            return Fail(ServiceResult<DriverTrip>.Conflict("Assign this bus to the driver before scheduling trips on it."));
        }
        var vehicleId = r.IndividualBusId ?? assignment.IndividualBusId;
        IndividualBus? vehicle = null;
        if (vehicleId is { } vid)
        {
            vehicle = await db.IndividualBuses.AsNoTracking().FirstOrDefaultAsync(v => v.Id == vid && v.BusId == busId);
            if (vehicle is null)
            {
                return Fail(ServiceResult<DriverTrip>.BadRequest("That vehicle does not run on this route."));
            }
            if (!string.Equals(vehicle.Status, "active", StringComparison.OrdinalIgnoreCase))
            {
                return Fail(ServiceResult<DriverTrip>.Conflict($"Vehicle {vehicle.BusCode} is not active."));
            }
        }

        var open = db.DriverTrips.Where(t => DriverTripStatus.Open.Contains(t.Status) && t.Id != editing
            && t.ScheduledDeparture < arrival && t.ScheduledArrival > departure);
        if (await open.AnyAsync(t => t.DriverId == driverId))
        {
            return Fail(ServiceResult<DriverTrip>.Conflict("The driver already has a trip at that time."));
        }
        if (vehicle is not null && await open.AnyAsync(t => t.IndividualBusId == vehicle.Id))
        {
            return Fail(ServiceResult<DriverTrip>.Conflict($"Vehicle {vehicle.BusCode} is already scheduled at that time."));
        }
        return (new Plan(assignment, bus, vehicle, date, departure, arrival, direction), null);
    }

    public async Task<ServiceResult<DriverTrip>> CreateAsync(Guid adminId, DriverScheduleRequest r, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", ScheduleLock);
            var (plan, problem) = await CheckAsync(r, now, null);
            if (problem is not null)
            {
                return problem;
            }
            var (start, end) = Ends(plan!.Bus, plan.Direction);
            var trip = new DriverTrip
            {
                Id = Guid.NewGuid(), DriverId = r.DriverId!.Value, BusId = plan.Bus.Id, IndividualBusId = plan.Vehicle?.Id, AssignmentId = plan.Assignment.Id,
                ScheduleDate = plan.Date, Direction = plan.Direction, StartStop = start, EndStop = end, ScheduledDeparture = plan.Departure,
                ScheduledArrival = plan.Arrival, Notes = Clean(r.Notes), CreatedByAdminId = adminId, UpdatedByAdminId = adminId, CreatedAt = now, UpdatedAt = now,
            };
            db.DriverTrips.Add(trip);
            await db.SaveChangesAsync();
            await RenumberAsync(trip.DriverId, trip.ScheduleDate);
            notifications.Notify(trip.DriverId, "New trip scheduled",
                $"{plan.Bus.BusName}: {start} → {end} on {plan.Date:dd MMM} at {DhakaTime.Clock(plan.Departure)}.",
                NotificationTypes.Info, $"/driver/trips/{trip.Id}", NotificationCategories.Account, adminId, trip.Id);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return ServiceResult<DriverTrip>.Created((await FindAsync(trip.Id))!, "Trip scheduled. The driver was notified.");
        });
    }

    // Only a scheduled trip that has not departed can be changed. The driver stays the same.
    public async Task<ServiceResult<DriverTrip>> UpdateAsync(Guid adminId, Guid id, DriverScheduleRequest r, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", ScheduleLock);
            var trip = await db.DriverTrips.FirstOrDefaultAsync(t => t.Id == id);
            if (trip is null)
            {
                return ServiceResult<DriverTrip>.NotFound("Schedule not found.");
            }
            if (trip.Status != DriverTripStatus.Scheduled || trip.ScheduledDeparture <= now)
            {
                return ServiceResult<DriverTrip>.Conflict("Only future trips that have not started can be changed.");
            }
            if (r.DriverId is { } other && other != trip.DriverId)
            {
                return ServiceResult<DriverTrip>.BadRequest("A trip cannot be moved to another driver. Cancel it and schedule a new one.");
            }
            r.DriverId = trip.DriverId;
            r.BusId ??= trip.BusId;
            var (plan, problem) = await CheckAsync(r, now, trip.Id);
            if (problem is not null)
            {
                return problem;
            }
            var oldDate = trip.ScheduleDate;
            (trip.StartStop, trip.EndStop) = Ends(plan!.Bus, plan.Direction);
            trip.BusId = plan.Bus.Id;
            trip.IndividualBusId = plan.Vehicle?.Id;
            trip.AssignmentId = plan.Assignment.Id;
            trip.ScheduleDate = plan.Date;
            trip.Direction = plan.Direction;
            trip.ScheduledDeparture = plan.Departure;
            trip.ScheduledArrival = plan.Arrival;
            trip.Notes = Clean(r.Notes) ?? trip.Notes;
            trip.ReminderSentAt = null;
            trip.UpdatedByAdminId = adminId;
            trip.UpdatedAt = now;
            notifications.Notify(trip.DriverId, "Schedule updated",
                $"Trip {trip.Code} now departs {plan.Date:dd MMM} at {DhakaTime.Clock(plan.Departure)} ({trip.StartStop} → {trip.EndStop}).",
                NotificationTypes.Info, $"/driver/trips/{trip.Id}", NotificationCategories.Account, adminId, trip.Id);
            await db.SaveChangesAsync();
            await RenumberAsync(trip.DriverId, trip.ScheduleDate);
            if (oldDate != trip.ScheduleDate)
            {
                await RenumberAsync(trip.DriverId, oldDate);
            }
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return ServiceResult<DriverTrip>.Ok((await FindAsync(trip.Id))!, "Schedule updated. The driver was notified.");
        });
    }

    public Task<ServiceResult<DriverTrip>> CancelAsync(Guid adminId, Guid id, string? reason) =>
        CloseAsync(adminId, id, DriverTripStatus.Cancelled, reason, mustHavePassed: false);

    // For a trip whose time has passed without it being started (the system also does this after the grace period).
    public Task<ServiceResult<DriverTrip>> MarkMissedAsync(Guid adminId, Guid id, string? reason) =>
        CloseAsync(adminId, id, DriverTripStatus.Missed, reason, mustHavePassed: true);

    private async Task<ServiceResult<DriverTrip>> CloseAsync(Guid adminId, Guid id, string to, string? reason, bool mustHavePassed)
    {
        var now = DateTime.UtcNow;
        var text = reason?.Trim() ?? "";
        if (text.Length < 3)
        {
            return ServiceResult<DriverTrip>.BadRequest("Give a short reason.");
        }
        var trip = await db.DriverTrips.Include(t => t.Bus).FirstOrDefaultAsync(t => t.Id == id);
        if (trip is null)
        {
            return ServiceResult<DriverTrip>.NotFound("Schedule not found.");
        }
        if (trip.Status != DriverTripStatus.Scheduled)
        {
            return ServiceResult<DriverTrip>.Conflict(trip.Status == DriverTripStatus.InProgress
                ? "This trip is in progress. Only the driver can complete it."
                : "This trip is already closed.");
        }
        if (mustHavePassed && trip.ScheduledDeparture > now)
        {
            return ServiceResult<DriverTrip>.Conflict("A trip can only be marked missed after its departure time.");
        }
        trip.Status = to;
        trip.CancelReason = text.Length > 500 ? text[..500] : text;
        trip.UpdatedByAdminId = adminId;
        trip.UpdatedAt = now;
        if (to == DriverTripStatus.Cancelled)
        {
            notifications.Notify(trip.DriverId, "Trip cancelled",
                $"Trip {trip.Code} ({trip.StartStop} → {trip.EndStop}, {trip.ScheduleDate:dd MMM} at {DhakaTime.Clock(trip.ScheduledDeparture)}) was cancelled: {text}",
                NotificationTypes.Warning, $"/driver/trips/{trip.Id}", NotificationCategories.Account, adminId, trip.Id);
        }
        await db.SaveChangesAsync();
        return ServiceResult<DriverTrip>.Ok((await FindAsync(trip.Id))!, to == DriverTripStatus.Cancelled ? "Trip cancelled. The driver was notified." : "Trip marked as missed.");
    }

    // Trip 1, 2, 3… in departure order for the driver's day.
    private async Task RenumberAsync(Guid driverId, DateOnly date)
    {
        var day = await db.DriverTrips.Where(t => t.DriverId == driverId && t.ScheduleDate == date).OrderBy(t => t.ScheduledDeparture).ThenBy(t => t.CreatedAt).ToListAsync();
        for (var i = 0; i < day.Count; i++)
        {
            day[i].TripNumber = i + 1;
        }
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim()[..Math.Min(text.Trim().Length, 500)];
}
