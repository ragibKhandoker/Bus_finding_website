using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

// Availability alerts and departure reminders for the user's favourite Dhaka City buses. Both name stops on the
// bus's own route, checked here. Another user's alert or reminder is reported as not found.
public class BusAlertService(AppDbContext db, BusRouteCache routes, DhakaBusGuard guard)
{
    public const int MaxAlertsPerUser = 30;
    public const int MaxRemindersPerUser = 50;

    // ---------------- Availability alerts ----------------

    public async Task<List<AvailabilityAlertViewModel>> AlertsAsync(Guid userId, CancellationToken ct)
    {
        var alerts = await db.BusAvailabilityAlerts.AsNoTracking().Where(a => a.UserId == userId).OrderByDescending(a => a.CreatedAt).ToListAsync(ct);
        var result = new List<AvailabilityAlertViewModel>(alerts.Count);
        foreach (var alert in alerts)
        {
            result.Add(AvailabilityAlertViewModel.From(alert, await routes.ByIdAsync(alert.BusId, ct)));
        }
        return result;
    }

    public async Task<ServiceResult<AvailabilityAlertViewModel>> CreateAlertAsync(Guid userId, AvailabilityAlertRequest request, CancellationToken ct)
    {
        var busId = Ids.Parse(request.BusId);
        var check = await guard.FavouriteCityBusAsync(userId, busId, ct);
        if (!check.Succeeded)
        {
            return check.As<AvailabilityAlertViewModel>();
        }
        var bus = check.Value!;
        var trip = Trip(bus, request.FromStop, request.ToStop);
        if (!trip.Succeeded)
        {
            return trip.As<AvailabilityAlertViewModel>();
        }
        if (await db.BusAvailabilityAlerts.CountAsync(a => a.UserId == userId, ct) >= MaxAlertsPerUser)
        {
            return ServiceResult<AvailabilityAlertViewModel>.Conflict($"You can have up to {MaxAlertsPerUser} availability alerts.");
        }
        var (from, to) = trip.Value;
        if (await db.BusAvailabilityAlerts.AnyAsync(a => a.UserId == userId && a.BusId == busId && a.FromStop == from && a.ToStop == to, ct))
        {
            return ServiceResult<AvailabilityAlertViewModel>.Conflict("You already have an alert for this bus and these stops.");
        }
        var seats = await FavouriteMonitor.FreeSeatsAsync(db, [busId], ct);
        var alert = new BusAvailabilityAlert
        {
            UserId = userId, BusId = busId, FromStop = from, ToStop = to, IsActive = request.IsActive ?? true,
            // The count now, so the first change after this is what gets reported.
            LastSeatsFree = seats.GetValueOrDefault(busId),
        };
        db.BusAvailabilityAlerts.Add(alert);
        if (!await TrySaveAsync(ct))
        {
            return ServiceResult<AvailabilityAlertViewModel>.Conflict("You already have an alert for this bus and these stops.");
        }
        return ServiceResult<AvailabilityAlertViewModel>.Created(AvailabilityAlertViewModel.From(alert, bus), "Availability alert saved.");
    }

    public async Task<ServiceResult<AvailabilityAlertViewModel>> UpdateAlertAsync(Guid userId, Guid alertId, AvailabilityAlertRequest request, CancellationToken ct)
    {
        var alert = await db.BusAvailabilityAlerts.FirstOrDefaultAsync(a => a.Id == alertId && a.UserId == userId, ct);
        if (alert is null)
        {
            return ServiceResult<AvailabilityAlertViewModel>.NotFound("Alert not found.");
        }
        var check = await guard.CityBusAsync(alert.BusId, ct);
        if (!check.Succeeded)
        {
            return check.As<AvailabilityAlertViewModel>();
        }
        if (request.FromStop is not null || request.ToStop is not null)
        {
            var trip = Trip(check.Value!, request.FromStop ?? alert.FromStop, request.ToStop ?? alert.ToStop);
            if (!trip.Succeeded)
            {
                return trip.As<AvailabilityAlertViewModel>();
            }
            (alert.FromStop, alert.ToStop) = trip.Value;
        }
        if (request.IsActive is { } active)
        {
            alert.IsActive = active;
        }
        alert.UpdatedAt = DateTime.UtcNow;
        if (!await TrySaveAsync(ct))
        {
            return ServiceResult<AvailabilityAlertViewModel>.Conflict("You already have an alert for this bus and these stops.");
        }
        return ServiceResult<AvailabilityAlertViewModel>.Ok(AvailabilityAlertViewModel.From(alert, check.Value), "Availability alert updated.");
    }

    public async Task<bool> DeleteAlertAsync(Guid userId, Guid alertId, CancellationToken ct) =>
        await db.BusAvailabilityAlerts.Where(a => a.Id == alertId && a.UserId == userId).ExecuteDeleteAsync(ct) > 0;

    // Both stops must be on the route, different, and in travel order (either way for a route that runs both ways).
    private static ServiceResult<(string From, string To)> Trip(BusViewModel bus, string? fromName, string? toName)
    {
        var from = DhakaBusGuard.StopOn(bus, fromName);
        var to = DhakaBusGuard.StopOn(bus, toName);
        if (from is null || to is null)
        {
            return ServiceResult<(string, string)>.BadRequest("Choose a starting stop and a destination stop on this bus's route.");
        }
        if (from.Order == to.Order)
        {
            return ServiceResult<(string, string)>.BadRequest("The starting stop and the destination must be different.");
        }
        if (!bus.RunsBothWays && from.Order > to.Order)
        {
            return ServiceResult<(string, string)>.BadRequest("This bus runs one way only: the destination must come after the starting stop.");
        }
        return ServiceResult<(string, string)>.Ok((from.Name, to.Name));
    }

    // ---------------- Departure reminders ----------------

    public async Task<List<BusReminderViewModel>> RemindersAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var reminders = await db.BusReminders.AsNoTracking().Where(r => r.UserId == userId).OrderBy(r => r.DepartureAt).ToListAsync(ct);
        var result = new List<BusReminderViewModel>(reminders.Count);
        foreach (var reminder in reminders)
        {
            result.Add(BusReminderViewModel.From(reminder, await routes.ByIdAsync(reminder.BusId, ct), now));
        }
        return result;
    }

    public async Task<ServiceResult<BusReminderViewModel>> CreateReminderAsync(Guid userId, BusReminderRequest request, CancellationToken ct)
    {
        var busId = Ids.Parse(request.BusId);
        var check = await guard.FavouriteCityBusAsync(userId, busId, ct);
        if (!check.Succeeded)
        {
            return check.As<BusReminderViewModel>();
        }
        if (await db.BusReminders.CountAsync(r => r.UserId == userId, ct) >= MaxRemindersPerUser)
        {
            return ServiceResult<BusReminderViewModel>.Conflict($"You can have up to {MaxRemindersPerUser} reminders. Delete old ones first.");
        }
        var reminder = new BusReminder { UserId = userId, BusId = busId };
        var problem = Apply(reminder, check.Value!, request, creating: true);
        if (problem is not null)
        {
            return ServiceResult<BusReminderViewModel>.BadRequest(problem);
        }
        db.BusReminders.Add(reminder);
        await db.SaveChangesAsync(ct);
        return ServiceResult<BusReminderViewModel>.Created(BusReminderViewModel.From(reminder, check.Value, DateTime.UtcNow), "Reminder saved.");
    }

    public async Task<ServiceResult<BusReminderViewModel>> UpdateReminderAsync(Guid userId, Guid reminderId, BusReminderRequest request, CancellationToken ct)
    {
        var reminder = await db.BusReminders.FirstOrDefaultAsync(r => r.Id == reminderId && r.UserId == userId, ct);
        if (reminder is null)
        {
            return ServiceResult<BusReminderViewModel>.NotFound("Reminder not found.");
        }
        var check = await guard.CityBusAsync(reminder.BusId, ct);
        if (!check.Succeeded)
        {
            return check.As<BusReminderViewModel>();
        }
        var problem = Apply(reminder, check.Value!, request, creating: false);
        if (problem is not null)
        {
            return ServiceResult<BusReminderViewModel>.BadRequest(problem);
        }
        reminder.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ServiceResult<BusReminderViewModel>.Ok(BusReminderViewModel.From(reminder, check.Value, DateTime.UtcNow), "Reminder updated.");
    }

    public async Task<bool> DeleteReminderAsync(Guid userId, Guid reminderId, CancellationToken ct) =>
        await db.BusReminders.Where(r => r.Id == reminderId && r.UserId == userId).ExecuteDeleteAsync(ct) > 0;

    // Copies the request onto the reminder; on update only the fields sent change. Returns the problem, if any.
    // A new time, stop or lead time makes it a fresh reminder that will be sent again.
    private static string? Apply(BusReminder reminder, BusViewModel bus, BusReminderRequest request, bool creating)
    {
        var resend = false;
        if (creating || request.StopName is not null)
        {
            var stop = DhakaBusGuard.StopOn(bus, request.StopName);
            if (stop is null)
            {
                return "Choose a stop on this bus's route.";
            }
            resend |= reminder.StopName != stop.Name;
            reminder.StopName = stop.Name;
        }
        if (creating || request.DepartureAt is not null)
        {
            if (DhakaTime.ParseToUtc(request.DepartureAt) is not { } departure)
            {
                return "Enter the departure date and time (Bangladesh time).";
            }
            if (departure <= DateTime.UtcNow.AddMinutes(1))
            {
                return "Choose a departure time in the future (Bangladesh time).";
            }
            if (departure > DateTime.UtcNow.AddYears(1))
            {
                return "Choose a departure time within the next year.";
            }
            resend |= reminder.DepartureAt != departure;
            reminder.DepartureAt = departure;
        }
        if (creating || request.LeadMinutes is not null)
        {
            var lead = request.LeadMinutes ?? 15;
            if (!BusReminder.LeadMinuteChoices.Contains(lead))
            {
                return "Remind me 15 minutes, 30 minutes or 1 hour before.";
            }
            resend |= reminder.LeadMinutes != lead;
            reminder.LeadMinutes = lead;
        }
        if (request.IsEnabled is { } enabled)
        {
            if (enabled && !reminder.IsEnabled && reminder.DepartureAt <= DateTime.UtcNow)
            {
                return "This departure has passed. Set a new time to turn the reminder back on.";
            }
            resend |= enabled && !reminder.IsEnabled;
            reminder.IsEnabled = enabled;
        }
        if (resend)
        {
            reminder.SentAt = null;
        }
        return null;
    }

    private async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
