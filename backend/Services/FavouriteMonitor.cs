using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

public record FavouriteMonitorReport(int BusesWatched, int FareChanges, int RouteChanges, int AvailabilityNotices, int RemindersSent);

// Watches favourited Dhaka City buses and tells their fans about real changes only. It never invents data: each
// check compares the buses table (fares, stops, vehicles) with what was seen last time (BusWatchState), and a
// notification is only written when the two differ. It also sends departure reminders when they fall due.
public class FavouriteMonitor(AppDbContext db, NotificationService notifications, BusRouteCache routes)
{
    public const string ActiveStatus = "active";

    // Starts watching a bus: remembers its fares, stops and active vehicles, and records its fare as the first
    // entry of its fare history. Queued on the context; the caller saves. Does nothing if it is already watched.
    public async Task EnsureWatchedAsync(Guid busId, CancellationToken ct)
    {
        if (db.BusWatchStates.Local.Any(s => s.BusId == busId) || await db.BusWatchStates.AnyAsync(s => s.BusId == busId, ct))
        {
            return;
        }
        var bus = await db.Buses.AsNoTracking().Include(b => b.Stops).FirstOrDefaultAsync(b => b.Id == busId, ct);
        if (bus is null)
        {
            return;
        }
        var now = DateTime.UtcNow;
        db.BusWatchStates.Add(new BusWatchState
        {
            BusId = bus.Id,
            BaseFare = bus.BaseFare,
            PerKmFare = bus.PerKmFare,
            StopNames = bus.Stops.OrderBy(s => s.Order).Select(s => s.Name).ToList(),
            ActiveVehicles = await db.IndividualBuses.CountAsync(v => v.BusId == busId && v.Status == ActiveStatus, ct),
            CheckedAt = now,
        });
        db.BusFareHistory.Add(new BusFareHistory { BusId = bus.Id, BaseFare = bus.BaseFare, PerKmFare = bus.PerKmFare, RecordedAt = now });
    }

    // Starts watching every favourited bus that is not watched yet (favourites saved before the module existed).
    public async Task<int> WatchAllFavouritesAsync(CancellationToken ct)
    {
        var missing = await db.FavoriteBuses.Select(f => f.BusId).Distinct()
            .Where(id => !db.BusWatchStates.Any(s => s.BusId == id)).ToListAsync(ct);
        foreach (var id in missing)
        {
            await EnsureWatchedAsync(id, ct);
        }
        return missing.Count;
    }

    // One full check. nowUtc is passed in so tests can control time.
    public async Task<FavouriteMonitorReport> RunOnceAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        await WatchAllFavouritesAsync(ct);
        await db.SaveChangesAsync(ct);

        var (watched, fareChanges, routeChanges) = await CheckWatchedBusesAsync(nowUtc, ct);
        var availability = await CheckAvailabilityAlertsAsync(nowUtc, ct);
        var reminders = await SendDueRemindersAsync(nowUtc, ct);
        await db.SaveChangesAsync(ct);
        if (fareChanges + routeChanges > 0)
        {
            // The in-memory route list must show the new fares and stops at once.
            routes.Invalidate();
        }
        return new FavouriteMonitorReport(watched, fareChanges, routeChanges, availability, reminders);
    }

    private static string Money(decimal value) => "৳" + value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Label(Bus bus) => bus.RouteNumber is { Length: > 0 } number ? $"{bus.BusName} ({number})" : bus.BusName;

    private async Task<(int Watched, int FareChanges, int RouteChanges)> CheckWatchedBusesAsync(DateTime now, CancellationToken ct)
    {
        // Only buses someone still has as a favourite are checked; others keep their last state until favourited again.
        var states = await db.BusWatchStates.Where(s => db.FavoriteBuses.Any(f => f.BusId == s.BusId)).ToListAsync(ct);
        if (states.Count == 0)
        {
            return (0, 0, 0);
        }
        var ids = states.Select(s => s.BusId).ToList();
        var buses = await db.Buses.AsNoTracking().Include(b => b.Stops).Where(b => ids.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
        var active = await db.IndividualBuses.Where(v => ids.Contains(v.BusId) && v.Status == ActiveStatus)
            .GroupBy(v => v.BusId).Select(g => new { BusId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.BusId, x => x.Count, ct);
        var fans = (await db.FavoriteBuses.Where(f => ids.Contains(f.BusId)).Select(f => new { f.BusId, f.UserId }).ToListAsync(ct))
            .ToLookup(f => f.BusId, f => f.UserId);

        int fareChanges = 0, routeChanges = 0;
        foreach (var state in states)
        {
            if (!buses.TryGetValue(state.BusId, out var bus))
            {
                continue;
            }
            var users = fans[state.BusId].ToList();
            var label = Label(bus);
            var link = bus.RouteNumber is { Length: > 0 } rn ? $"/bus-routes/{Uri.EscapeDataString(rn)}" : "/favourites";

            // Fare: the buses table is the fare database; any difference from last time is a real change.
            if (bus.BaseFare != state.BaseFare || bus.PerKmFare != state.PerKmFare)
            {
                fareChanges++;
                db.BusFareHistory.Add(new BusFareHistory
                {
                    BusId = bus.Id, BaseFare = bus.BaseFare, PerKmFare = bus.PerKmFare,
                    PreviousBaseFare = state.BaseFare, PreviousPerKmFare = state.PerKmFare, RecordedAt = now,
                });
                var diff = bus.BaseFare - state.BaseFare;
                var (title, type, change) = diff < 0 ? ("Dhaka bus fare decreased", NotificationTypes.Success, $" ({Money(-diff)} less)")
                    : diff > 0 ? ("Dhaka bus fare increased", NotificationTypes.Warning, $" ({Money(diff)} more)")
                    : ("Dhaka bus fare updated", NotificationTypes.Info, "");
                var message = diff != 0
                    ? $"The base fare of {label} changed from {Money(state.BaseFare)} to {Money(bus.BaseFare)}{change}."
                    : $"The per-km fare of {label} changed from {Money(state.PerKmFare)} to {Money(bus.PerKmFare)}.";
                foreach (var user in users)
                {
                    notifications.Notify(user, title, message, type, "/favourites", NotificationCategories.BusFare, relatedId: bus.Id);
                }
                state.BaseFare = bus.BaseFare;
                state.PerKmFare = bus.PerKmFare;
            }

            // Route: stops added, removed or reordered.
            var stops = bus.Stops.OrderBy(s => s.Order).Select(s => s.Name).ToList();
            if (!stops.SequenceEqual(state.StopNames))
            {
                routeChanges++;
                var added = stops.Except(state.StopNames).ToList();
                var removed = state.StopNames.Except(stops).ToList();
                var parts = new List<string>();
                if (added.Count > 0) parts.Add($"new stop{(added.Count > 1 ? "s" : "")}: {string.Join(", ", added)}");
                if (removed.Count > 0) parts.Add($"removed stop{(removed.Count > 1 ? "s" : "")}: {string.Join(", ", removed)}");
                if (parts.Count == 0) parts.Add("the order of its stops changed");
                var ends = stops.Count > 1 ? $" between {stops[0]} and {stops[^1]}" : "";
                var message = $"Your favourite bus {label}{ends} has an updated route ({string.Join("; ", parts)}). Please review the new stops.";
                if (!DhakaCityArea.Default.IsCityRoute(bus))
                {
                    message += " It now runs outside Dhaka City, so it is no longer tracked by your favourite bus tools.";
                }
                foreach (var user in users)
                {
                    notifications.Notify(user, "Dhaka route updated", message, NotificationTypes.Warning, link, NotificationCategories.BusRoute, relatedId: bus.Id);
                }
                state.StopNames = stops;
            }

            // Service: every vehicle of the route taken off the road, or back on it.
            var running = active.GetValueOrDefault(bus.Id);
            if ((state.ActiveVehicles > 0) != (running > 0))
            {
                routeChanges++;
                var (title, message) = running == 0
                    ? ("Dhaka bus unavailable", $"{label} has no buses in service right now.")
                    : ("Dhaka bus back in service", $"{label} has {running} bus{(running > 1 ? "es" : "")} in service again.");
                foreach (var user in users)
                {
                    notifications.Notify(user, title, message, running == 0 ? NotificationTypes.Warning : NotificationTypes.Success, link, NotificationCategories.BusRoute, relatedId: bus.Id);
                }
            }
            state.ActiveVehicles = running;
            state.CheckedAt = now;
        }
        return (states.Count, fareChanges, routeChanges);
    }

    // Free seats on a route's active vehicles, from the passenger counts riders update when they board and leave.
    public static IQueryable<IGrouping<Guid, IndividualBus>> ActiveVehiclesByBus(AppDbContext db, ICollection<Guid> busIds) =>
        db.IndividualBuses.Where(v => busIds.Contains(v.BusId) && v.Status == ActiveStatus).GroupBy(v => v.BusId);

    public static async Task<Dictionary<Guid, int>> FreeSeatsAsync(AppDbContext db, ICollection<Guid> busIds, CancellationToken ct) =>
        await ActiveVehiclesByBus(db, busIds)
            .Select(g => new { BusId = g.Key, Free = g.Sum(v => v.TotalPassengerCapacity > v.CurrentPassengerCount ? v.TotalPassengerCapacity - v.CurrentPassengerCount : 0) })
            .ToDictionaryAsync(x => x.BusId, x => x.Free, ct);

    private async Task<int> CheckAvailabilityAlertsAsync(DateTime now, CancellationToken ct)
    {
        var alerts = await db.BusAvailabilityAlerts.Include(a => a.Bus).Where(a => a.IsActive).ToListAsync(ct);
        if (alerts.Count == 0)
        {
            return 0;
        }
        var seats = await FreeSeatsAsync(db, alerts.Select(a => a.BusId).Distinct().ToList(), ct);
        var sent = 0;
        foreach (var alert in alerts)
        {
            var free = seats.GetValueOrDefault(alert.BusId);
            // Only a change between "no seats" and "seats free" is news; the first check just records the count.
            if (alert.LastSeatsFree is { } before && (before == 0) != (free == 0) && alert.Bus is { } bus)
            {
                sent++;
                var trip = $"{alert.FromStop} → {alert.ToStop}";
                var (title, message) = free > 0
                    ? ("Bus availability updated", $"Seats are free again on {Label(bus)} ({free} free across its buses in service). Your alert: {trip}.")
                    : ("Bus availability updated", $"{Label(bus)} is full right now: no free seats on its buses in service. Your alert: {trip}.");
                notifications.Notify(alert.UserId, title, message, free > 0 ? NotificationTypes.Success : NotificationTypes.Warning,
                    "/favourites", NotificationCategories.BusAvailability, relatedId: alert.Id);
                alert.LastNotifiedAt = now;
            }
            alert.LastSeatsFree = free;
        }
        return sent;
    }

    private async Task<int> SendDueRemindersAsync(DateTime now, CancellationToken ct)
    {
        var horizon = now.AddMinutes(BusReminder.LeadMinuteChoices.Max());
        var candidates = await db.BusReminders.Include(r => r.Bus)
            .Where(r => r.IsEnabled && r.SentAt == null && r.DepartureAt > now && r.DepartureAt <= horizon)
            .ToListAsync(ct);
        var sent = 0;
        foreach (var reminder in candidates.Where(r => r.DepartureAt.AddMinutes(-r.LeadMinutes) <= now))
        {
            sent++;
            var bus = reminder.Bus is { } b ? Label(b) : "your bus";
            var minutes = (int)Math.Round((reminder.DepartureAt - now).TotalMinutes);
            notifications.Notify(reminder.UserId, "Departure reminder",
                $"{bus} from {reminder.StopName} at {DhakaTime.Clock(reminder.DepartureAt)} (Bangladesh time), in about {minutes} minute{(minutes == 1 ? "" : "s")}.",
                NotificationTypes.Info, "/favourites", NotificationCategories.BusReminder, relatedId: reminder.Id);
            reminder.SentAt = now;
        }
        return sent;
    }
}

// Runs the monitor every minute (FavouriteMonitor:IntervalSeconds) while the server is up. Turned off with
// FavouriteMonitor:Enabled = false (the integration tests run it by hand instead).
public class FavouriteMonitorHostedService(IServiceScopeFactory scopes, IConfiguration config, ILogger<FavouriteMonitorHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("FavouriteMonitor:Enabled", true))
        {
            return;
        }
        var interval = TimeSpan.FromSeconds(Math.Max(15, config.GetValue("FavouriteMonitor:IntervalSeconds", 60)));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<FavouriteMonitor>().RunOnceAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Favourite bus monitor check failed; it will try again.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
