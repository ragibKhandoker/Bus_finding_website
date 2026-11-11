using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Extensions;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

public record DriverTripFilter(DateOnly? From = null, DateOnly? To = null, string? Status = null, string? Search = null, bool Oldest = false,
    int Page = 1, int PageSize = 20, bool UpcomingOnly = false, bool PastOnly = false);

public record DriverDayCount(string Date, int Completed, int Missed, int Cancelled, int Scheduled);

public record DriverPeriodCount(string Label, string From, string To, int Completed, int Missed, int Cancelled, int DrivingMinutes);

public record DriverTripStats(int TotalAssigned, int Completed, int Cancelled, int Missed, int InProgress, int Scheduled, double? CompletionRate,
    int TotalDrivingMinutes, int CompletedThisWeek, int CompletedThisMonth, int TripsThisWeek, int TripsThisMonth,
    List<DriverDayCount> Daily, List<DriverPeriodCount> Weekly, List<DriverPeriodCount> Monthly);

// What the driver sees about one trip, worked out with the current time and the driver's active assignments.
public record DriverTripState(DriverTrip Trip, bool CanStart, bool CanComplete, DateTime StartOpensAt, DateTime MissedAt, bool AssignmentActive);

// F22, the driver's side: schedules, starting and completing trips, working status and statistics. The driver is
// always the user in the token; another driver's trip is answered as not found. Start and complete are each one
// transaction with a conditional, row-versioned update, so two requests at once (a double click, two devices)
// cannot both succeed, and a driver can never have two trips in progress (also enforced by a unique index).
public class DriverTripService(AppDbContext db, NotificationService notifications, IOptions<DriverTripOptions> options)
{
    private DriverTripOptions Options => options.Value;

    public static DateOnly Today(DateTime utcNow) => DateOnly.FromDateTime(DhakaTime.FromUtc(utcNow));

    // ---- System rules: missed trips and reminders ----

    // A scheduled trip nobody started within MissedAfterMinutes of its departure becomes MISSED, and drivers get one
    // reminder ReminderMinutesBefore departure. Run by the background job and before driver and admin reads.
    public async Task<int> SweepAsync(DateTime now, CancellationToken ct = default)
    {
        var missedBefore = now.AddMinutes(-Options.MissedAfterMinutes);
        var missed = await db.DriverTrips
            .Where(t => t.Status == DriverTripStatus.Scheduled && t.ScheduledDeparture < missedBefore)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, DriverTripStatus.Missed).SetProperty(t => t.UpdatedAt, now), ct);

        var remindUntil = now.AddMinutes(Options.ReminderMinutesBefore);
        var due = await db.DriverTrips.Include(t => t.Bus)
            .Where(t => t.Status == DriverTripStatus.Scheduled && t.ReminderSentAt == null && t.ScheduledDeparture > now && t.ScheduledDeparture <= remindUntil)
            .OrderBy(t => t.ScheduledDeparture).ThenBy(t => t.Id).Take(200).ToListAsync(ct);
        foreach (var trip in due)
        {
            trip.ReminderSentAt = now;
            notifications.Notify(trip.DriverId, "Upcoming duty reminder",
                $"Trip {trip.TripNumber} on {trip.Bus?.BusName} ({trip.StartStop} → {trip.EndStop}) departs at {DhakaTime.Clock(trip.ScheduledDeparture)}.",
                NotificationTypes.Info, $"/driver/trips/{trip.Id}", NotificationCategories.Account, null, trip.Id);
        }
        if (due.Count > 0)
        {
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // The trip changed meanwhile (started, edited); the next run tries again.
                db.ChangeTracker.Clear();
            }
        }
        return missed;
    }

    // Reads run the sweep at most every 20 seconds (per server); the background job runs it every minute anyway, and
    // starting a trip checks the missed time itself. This keeps two extra queries off almost every page load.
    private static long _lastSweepTicks;
    public Task SweepIfDueAsync(DateTime now)
    {
        var tick = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastSweepTicks);
        if (last != 0 && tick - last < 20_000)
        {
            return Task.CompletedTask;
        }
        Interlocked.Exchange(ref _lastSweepTicks, tick);
        return SweepAsync(now);
    }

    // ---- Reading ----

    private IQueryable<DriverTrip> Full => db.DriverTrips.Include(t => t.Bus).Include(t => t.IndividualBus).Include(t => t.Driver);

    public Task<List<Guid>> ActiveBusIdsAsync(Guid driverId) =>
        db.DriverBusAssignments.Where(a => a.DriverId == driverId && a.IsActive).Select(a => a.BusId).ToListAsync();

    public DriverTripState StateOf(DriverTrip trip, ICollection<Guid> activeBusIds, DateTime now)
    {
        var opens = trip.ScheduledDeparture.AddMinutes(-Options.StartWindowMinutesBefore);
        var missedAt = trip.ScheduledDeparture.AddMinutes(Options.MissedAfterMinutes);
        var assignmentActive = activeBusIds.Contains(trip.BusId);
        var canStart = trip.Status == DriverTripStatus.Scheduled && assignmentActive && now >= opens && now < missedAt;
        return new DriverTripState(trip, canStart, trip.Status == DriverTripStatus.InProgress, opens, missedAt, assignmentActive);
    }

    public async Task<(List<DriverTrip> Items, int Total)> TripsAsync(Guid driverId, DriverTripFilter f, DateTime now)
    {
        await SweepIfDueAsync(now);
        var query = db.DriverTrips.AsNoTracking().Where(t => t.DriverId == driverId);
        if (f.From is { } from) query = query.Where(t => t.ScheduleDate >= from);
        if (f.To is { } to) query = query.Where(t => t.ScheduleDate <= to);
        if (f.Status is { Length: > 0 } status) query = query.Where(t => t.Status == status);
        if (f.UpcomingOnly) query = query.Where(t => t.Status == DriverTripStatus.Scheduled && t.ScheduledDeparture >= now.AddMinutes(-Options.MissedAfterMinutes));
        if (f.PastOnly) query = query.Where(t => t.Status != DriverTripStatus.Scheduled && t.Status != DriverTripStatus.InProgress);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var like = f.Search.ContainsPattern();
            query = query.Where(t => EF.Functions.ILike(t.Bus!.BusName, like) || EF.Functions.ILike(t.StartStop, like)
                || EF.Functions.ILike(t.EndStop, like) || EF.Functions.ILike(t.Bus.RouteNumber!, like));
        }
        var total = await query.CountAsync();
        var ordered = f.Oldest ? query.OrderBy(t => t.ScheduledDeparture).ThenBy(t => t.Id) : query.OrderByDescending(t => t.ScheduledDeparture).ThenByDescending(t => t.Id);
        var items = await ordered.Include(t => t.Bus).Include(t => t.IndividualBus).Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).ToListAsync();
        return (items, total);
    }

    // Only the driver's own trip; anyone else's is null (answered as not found).
    public async Task<DriverTrip?> TripAsync(Guid driverId, Guid tripId, DateTime now)
    {
        await SweepIfDueAsync(now);
        return await Full.Include(t => t.Bus!.Stops).AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripId && t.DriverId == driverId);
    }

    public Task<DriverTrip?> ActiveTripAsync(Guid driverId) =>
        Full.AsNoTracking().FirstOrDefaultAsync(t => t.DriverId == driverId && t.Status == DriverTripStatus.InProgress);

    // The routes of the driver's active assignments and of their open trips, with stops.
    public async Task<List<Bus>> RoutesAsync(Guid driverId)
    {
        var assigned = db.DriverBusAssignments.Where(a => a.DriverId == driverId && a.IsActive).Select(a => a.BusId);
        var scheduled = db.DriverTrips.Where(t => t.DriverId == driverId && DriverTripStatus.Open.Contains(t.Status)).Select(t => t.BusId);
        return await db.Buses.Include(b => b.Stops).AsNoTracking()
            .Where(b => assigned.Contains(b.Id) || scheduled.Contains(b.Id)).OrderBy(b => b.BusName).ToListAsync();
    }

    public async Task<Bus?> RouteAsync(Guid driverId, Guid routeId) =>
        (await RoutesAsync(driverId)).FirstOrDefault(b => b.Id == routeId);

    public Task<List<DriverBusAssignment>> AssignmentsAsync(Guid driverId) => db.DriverBusAssignments
        .Include(a => a.Bus!).ThenInclude(b => b.Stops).Include(a => a.IndividualBus).AsNoTracking()
        .Where(a => a.DriverId == driverId).OrderByDescending(a => a.IsActive).ThenByDescending(a => a.AssignedAt).ToListAsync();

    // ---- Dashboard: everything in about six queries ----

    public record DashboardData(DriverProfile Profile, string Name, string Email, string Phone, bool HasPhoto, DriverWorkingStatus Status,
        List<DriverBusAssignment> Assignments, List<DriverTrip> Today, List<DriverTrip> Upcoming, int UpcomingTotal, DriverTrip? Active,
        List<Bus> Routes, int Completed, int CompletedWeek, int CompletedMonth, double? CompletionRate, List<Guid> ActiveBusIds);

    public async Task<DashboardData?> DashboardAsync(Guid driverId, DateTime now)
    {
        await SweepIfDueAsync(now);
        var today = Today(now);
        var weekStart = today.AddDays(-6);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var upcomingFrom = now.AddMinutes(-Options.MissedAfterMinutes);

        var who = await db.DriverProfiles.AsNoTracking().Where(p => p.UserId == driverId).Select(p => new
        {
            Profile = p, p.User!.Name, p.User.Email, p.User.Phone,
            HasPhoto = p.Documents.Any(d => d.DocumentType == DriverDocumentTypes.ProfilePhoto),
        }).FirstOrDefaultAsync();
        if (who is null)
        {
            return null;
        }
        var assignments = await db.DriverBusAssignments.AsNoTracking().Include(a => a.Bus!).ThenInclude(b => b.Stops).Include(a => a.IndividualBus)
            .Where(a => a.DriverId == driverId && a.IsActive).OrderByDescending(a => a.AssignedAt).ToListAsync();
        // Today's trips and every open trip, in one query.
        var trips = await db.DriverTrips.AsNoTracking().Include(t => t.Bus).Include(t => t.IndividualBus)
            .Where(t => t.DriverId == driverId && (t.ScheduleDate == today || t.Status == DriverTripStatus.Scheduled || t.Status == DriverTripStatus.InProgress))
            .OrderBy(t => t.ScheduledDeparture).ThenBy(t => t.Id).ToListAsync();
        var counts = await db.DriverTrips.AsNoTracking().Where(t => t.DriverId == driverId)
            .GroupBy(t => t.DriverId).Select(g => new
            {
                Completed = g.Count(t => t.Status == DriverTripStatus.Completed),
                Week = g.Count(t => t.Status == DriverTripStatus.Completed && t.ScheduleDate >= weekStart && t.ScheduleDate <= today),
                Month = g.Count(t => t.Status == DriverTripStatus.Completed && t.ScheduleDate >= monthStart && t.ScheduleDate <= today),
                Missed = g.Count(t => t.Status == DriverTripStatus.Missed),
            }).FirstOrDefaultAsync();
        var status = await StatusAsync(driverId);

        var upcoming = trips.Where(t => t.Status == DriverTripStatus.Scheduled && t.ScheduledDeparture >= upcomingFrom).ToList();
        var routes = assignments.Select(a => a.Bus!).ToList();
        var missingRoutes = trips.Where(t => DriverTripStatus.Open.Contains(t.Status)).Select(t => t.BusId).Distinct().Where(id => routes.All(b => b.Id != id)).ToList();
        if (missingRoutes.Count > 0)
        {
            routes.AddRange(await db.Buses.AsNoTracking().Include(b => b.Stops).Where(b => missingRoutes.Contains(b.Id)).ToListAsync());
        }
        var completed = counts?.Completed ?? 0;
        var missed = counts?.Missed ?? 0;
        return new DashboardData(who.Profile, who.Name, who.Email, who.Phone ?? "", who.HasPhoto, status, assignments,
            trips.Where(t => t.ScheduleDate == today).ToList(), upcoming.Take(5).ToList(), upcoming.Count,
            trips.FirstOrDefault(t => t.Status == DriverTripStatus.InProgress), routes.OrderBy(b => b.BusName).ToList(),
            completed, counts?.Week ?? 0, counts?.Month ?? 0, completed + missed == 0 ? null : Math.Round(100.0 * completed / (completed + missed), 1),
            assignments.Select(a => a.BusId).ToList());
    }

    // ---- Working status ----

    public async Task<DriverWorkingStatus> StatusAsync(Guid driverId) =>
        await db.DriverWorkingStatuses.AsNoTracking().FirstOrDefaultAsync(s => s.DriverId == driverId)
        ?? new DriverWorkingStatus { DriverId = driverId, CurrentStatus = DriverWorkStatus.OffDuty, LastStatusChangedAt = DateTime.MinValue };

    public Task<List<DriverStatusHistory>> StatusHistoryAsync(Guid driverId, int take = 15) => db.DriverStatusHistory.AsNoTracking()
        .Where(h => h.DriverId == driverId).OrderByDescending(h => h.ChangedAt).Take(take).ToListAsync();

    // Changes the status on the current context (the caller saves) and records the change.
    private async Task ChangeStatusAsync(Guid driverId, string to, string reason, string by, DateTime now, Guid? tripId = null)
    {
        var row = await db.DriverWorkingStatuses.FirstOrDefaultAsync(s => s.DriverId == driverId);
        var from = row?.CurrentStatus;
        if (row is null)
        {
            row = new DriverWorkingStatus { DriverId = driverId };
            db.DriverWorkingStatuses.Add(row);
        }
        if (from == to)
        {
            return;
        }
        row.CurrentStatus = to;
        row.LastStatusChangedAt = now;
        row.UpdatedAt = now;
        db.DriverStatusHistory.Add(new DriverStatusHistory { DriverId = driverId, PreviousStatus = from, NewStatus = to, Reason = reason, ChangedBy = by, TripId = tripId, ChangedAt = now });
    }

    // The driver choosing a status. ON_TRIP is never chosen by hand, and nothing can be chosen during a trip.
    public async Task<ServiceResult<DriverWorkingStatus>> SetStatusAsync(Guid driverId, string? status, string? reason, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var to = status?.Trim().ToUpperInvariant().Replace(' ', '_').Replace('-', '_');
        if (to == DriverWorkStatus.OnTrip)
        {
            return ServiceResult<DriverWorkingStatus>.BadRequest("On Trip is set automatically when you start a trip.");
        }
        if (to is null || !DriverWorkStatus.Manual.Contains(to))
        {
            return ServiceResult<DriverWorkingStatus>.BadRequest("Choose Available, On Duty, Break, Off Duty or Unavailable.");
        }
        if (await db.DriverTrips.AnyAsync(t => t.DriverId == driverId && t.Status == DriverTripStatus.InProgress))
        {
            return ServiceResult<DriverWorkingStatus>.Conflict(to == DriverWorkStatus.OffDuty
                ? "You cannot go off duty while a trip is in progress. Complete the trip first."
                : "Your status stays On Trip until you complete the current trip.");
        }
        var note = string.IsNullOrWhiteSpace(reason) ? "Changed by the driver." : reason.Trim()[..Math.Min(reason.Trim().Length, 300)];
        await ChangeStatusAsync(driverId, to, note, "driver", now);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } })
        {
            return ServiceResult<DriverWorkingStatus>.Conflict("Your status changed at the same moment. Please try again.");
        }
        return ServiceResult<DriverWorkingStatus>.Ok(await StatusAsync(driverId), "Working status updated.");
    }

    // ---- Starting and completing ----

    public async Task<ServiceResult<DriverTrip>> StartAsync(Guid driverId, Guid tripId, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var trip = await db.DriverTrips.Include(t => t.Bus).FirstOrDefaultAsync(t => t.Id == tripId && t.DriverId == driverId);
            if (trip is null)
            {
                return ServiceResult<DriverTrip>.NotFound("Trip not found.");
            }
            var problem = trip.Status switch
            {
                DriverTripStatus.InProgress => "This trip has already started.",
                DriverTripStatus.Completed => "This trip is already completed and cannot be started again.",
                DriverTripStatus.Cancelled => "This trip was cancelled by an administrator.",
                DriverTripStatus.Missed => "This trip was missed and can no longer be started.",
                _ => null,
            };
            if (problem is not null)
            {
                return ServiceResult<DriverTrip>.Conflict(problem);
            }
            if (!await db.DriverBusAssignments.AnyAsync(a => a.DriverId == driverId && a.BusId == trip.BusId && a.IsActive))
            {
                return ServiceResult<DriverTrip>.Forbidden("Your assignment to this bus is no longer active, so you cannot start trips on it.");
            }
            var state = StateOf(trip, [trip.BusId], now);
            if (now < state.StartOpensAt)
            {
                return ServiceResult<DriverTrip>.Conflict(
                    $"This trip can be started from {DhakaTime.Clock(state.StartOpensAt)} ({Options.StartWindowMinutesBefore} minutes before departure).");
            }
            if (now >= state.MissedAt)
            {
                return ServiceResult<DriverTrip>.Conflict("The start time for this trip has passed.");
            }
            if (await db.DriverTrips.AnyAsync(t => t.DriverId == driverId && t.Status == DriverTripStatus.InProgress))
            {
                return ServiceResult<DriverTrip>.Conflict("You already have a trip in progress. Complete it before starting another.");
            }
            var status = (await db.DriverWorkingStatuses.AsNoTracking().FirstOrDefaultAsync(s => s.DriverId == driverId))?.CurrentStatus ?? DriverWorkStatus.OffDuty;
            if (!DriverWorkStatus.CanStartTrip.Contains(status))
            {
                return ServiceResult<DriverTrip>.Conflict("Set your working status to Available or On Duty before starting a trip.");
            }

            trip.Status = DriverTripStatus.InProgress;
            trip.ActualDeparture = now;
            trip.UpdatedAt = now;
            await ChangeStatusAsync(driverId, DriverWorkStatus.OnTrip, $"Started trip {trip.TripNumber} ({trip.Code}).", "system", now, trip.Id);
            try
            {
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return ServiceResult<DriverTrip>.Conflict("This trip was just started or changed. Refresh to see its status.");
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return ServiceResult<DriverTrip>.Conflict("You already have a trip in progress. Complete it before starting another.");
            }
            return ServiceResult<DriverTrip>.Ok(trip, "Trip started. Drive safely!");
        });
    }

    public async Task<ServiceResult<DriverTrip>> CompleteAsync(Guid driverId, Guid tripId, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var trip = await db.DriverTrips.Include(t => t.Bus).FirstOrDefaultAsync(t => t.Id == tripId && t.DriverId == driverId);
            if (trip is null)
            {
                return ServiceResult<DriverTrip>.NotFound("Trip not found.");
            }
            var problem = trip.Status switch
            {
                DriverTripStatus.Scheduled => "You have not started this trip yet.",
                DriverTripStatus.Completed => "This trip is already completed.",
                DriverTripStatus.Cancelled => "This trip was cancelled.",
                DriverTripStatus.Missed => "This trip was missed and was never started.",
                _ => null,
            };
            if (problem is not null)
            {
                return ServiceResult<DriverTrip>.Conflict(problem);
            }

            var started = trip.ActualDeparture ?? now;
            trip.Status = DriverTripStatus.Completed;
            trip.ActualArrival = now < started ? started : now;
            trip.DurationMinutes = (int)Math.Round((trip.ActualArrival.Value - started).TotalMinutes);
            trip.UpdatedAt = now;
            // Back on duty while more trips are scheduled today; otherwise available.
            var today = Today(now);
            var more = await db.DriverTrips.AnyAsync(t => t.DriverId == driverId && t.Id != trip.Id && t.Status == DriverTripStatus.Scheduled && t.ScheduleDate == today);
            await ChangeStatusAsync(driverId, more ? DriverWorkStatus.OnDuty : DriverWorkStatus.Available,
                $"Completed trip {trip.TripNumber} ({trip.Code}).", "system", now, trip.Id);
            notifications.Notify(driverId, "Trip completed successfully!",
                $"Trip {trip.TripNumber} on {trip.Bus?.BusName} ({trip.StartStop} → {trip.EndStop}) took {trip.DurationMinutes} minutes.",
                NotificationTypes.Success, $"/driver/trips/{trip.Id}", NotificationCategories.Account, null, trip.Id);
            try
            {
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return ServiceResult<DriverTrip>.Conflict("This trip was just completed. Refresh to see it.");
            }
            return ServiceResult<DriverTrip>.Ok(trip, "Trip completed successfully!");
        });
    }

    // ---- Statistics ----

    // Every count is over the driver's own trips, each trip counted once by its schedule date (Dhaka time).
    public async Task<DriverTripStats> StatsAsync(Guid driverId, DateTime now)
    {
        await SweepIfDueAsync(now);
        var rows = await db.DriverTrips.AsNoTracking().Where(t => t.DriverId == driverId)
            .Select(t => new { t.Id, t.Status, t.ScheduleDate, t.DurationMinutes }).ToListAsync();
        var today = Today(now);
        int Count(string status) => rows.Count(r => r.Status == status);
        var completed = Count(DriverTripStatus.Completed);
        var missed = Count(DriverTripStatus.Missed);
        var weekStart = today.AddDays(-6);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        DriverPeriodCount Period(string label, DateOnly from, DateOnly to)
        {
            var inRange = rows.Where(r => r.ScheduleDate >= from && r.ScheduleDate <= to).ToList();
            return new DriverPeriodCount(label, from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"),
                inRange.Count(r => r.Status == DriverTripStatus.Completed), inRange.Count(r => r.Status == DriverTripStatus.Missed),
                inRange.Count(r => r.Status == DriverTripStatus.Cancelled),
                inRange.Where(r => r.Status == DriverTripStatus.Completed).Sum(r => r.DurationMinutes ?? 0));
        }

        var daily = Enumerable.Range(0, 7).Select(i => today.AddDays(i - 6)).Select(d =>
        {
            var day = rows.Where(r => r.ScheduleDate == d).ToList();
            return new DriverDayCount(d.ToString("yyyy-MM-dd"), day.Count(r => r.Status == DriverTripStatus.Completed),
                day.Count(r => r.Status == DriverTripStatus.Missed), day.Count(r => r.Status == DriverTripStatus.Cancelled),
                day.Count(r => DriverTripStatus.Open.Contains(r.Status)));
        }).ToList();
        var weekly = Enumerable.Range(0, 8).Select(i => today.AddDays(-7 * (7 - i)))
            .Select(end => Period($"{end.AddDays(-6):dd MMM} – {end:dd MMM}", end.AddDays(-6), end)).ToList();
        var monthly = Enumerable.Range(0, 6).Select(i => monthStart.AddMonths(i - 5))
            .Select(m => Period(m.ToString("MMM yyyy"), m, m.AddMonths(1).AddDays(-1))).ToList();

        return new DriverTripStats(
            TotalAssigned: rows.Count, Completed: completed, Cancelled: Count(DriverTripStatus.Cancelled), Missed: missed,
            InProgress: Count(DriverTripStatus.InProgress), Scheduled: Count(DriverTripStatus.Scheduled),
            CompletionRate: completed + missed == 0 ? null : Math.Round(100.0 * completed / (completed + missed), 1),
            TotalDrivingMinutes: rows.Where(r => r.Status == DriverTripStatus.Completed).Sum(r => r.DurationMinutes ?? 0),
            CompletedThisWeek: rows.Count(r => r.Status == DriverTripStatus.Completed && r.ScheduleDate >= weekStart && r.ScheduleDate <= today),
            CompletedThisMonth: rows.Count(r => r.Status == DriverTripStatus.Completed && r.ScheduleDate >= monthStart && r.ScheduleDate <= today),
            TripsThisWeek: rows.Count(r => r.ScheduleDate >= weekStart && r.ScheduleDate <= today),
            TripsThisMonth: rows.Count(r => r.ScheduleDate >= monthStart && r.ScheduleDate < monthStart.AddMonths(1)),
            Daily: daily, Weekly: weekly, Monthly: monthly);
    }
}

public class DriverTripLifecycleHostedService(IServiceScopeFactory scopes, IOptions<DriverTripOptions> options, ILogger<DriverTripLifecycleHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.LifecycleEnabled)
        {
            return;
        }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(15, options.Value.LifecycleIntervalSeconds)));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<DriverTripService>().SweepAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Driver trip check failed; it will try again.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
