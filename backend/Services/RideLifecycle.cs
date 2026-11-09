using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

// Moves rides along in time, on the server (the list never relies on the browser to hide old rides):
//  - a "leaves soon" reminder to the host and approved riders ReminderLead before departure, once;
//  - OPEN/FULL rides become EXPIRED once departure has passed: at once when nobody was approved (the ride did not
//    happen), or CompletionGrace after departure when riders were approved but the host never marked it completed.
// Pending requests on an expired ride are closed. RunOnceAsync is idempotent, so running it twice changes nothing.
public class RideLifecycle(AppDbContext db, NotificationService notifications)
{
    public static readonly TimeSpan ReminderLead = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan CompletionGrace = TimeSpan.FromHours(12);

    public record Outcome(int Reminders, int Expired);

    public async Task<Outcome> RunOnceAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var reminders = await RemindAsync(nowUtc, ct);
        var expired = await ExpireAsync(nowUtc, ct);
        return new Outcome(reminders, expired);
    }

    private async Task<int> RemindAsync(DateTime now, CancellationToken ct)
    {
        var soon = now + ReminderLead;
        var due = await db.RidePosts.Include(p => p.Requests)
            .Where(p => p.DeletedAt == null && (p.Status == RideStatus.Active || p.Status == RideStatus.Full)
                        && p.ReminderSentAt == null && p.DepartureAt > now && p.DepartureAt <= soon)
            .ToListAsync(ct);
        foreach (var post in due)
        {
            var riders = post.Requests.Where(r => r.Status == RideRequestStatus.Accepted).Select(r => r.RequesterId).ToList();
            var when = DhakaTime.Clock(post.DepartureAt!.Value);
            notifications.Notify(post.UserId, "Your ride leaves soon",
                riders.Count == 0
                    ? $"Your ride from {post.Source} to {post.Destination} leaves at {when}. No riders are approved yet."
                    : $"Your ride from {post.Source} to {post.Destination} leaves at {when} with {riders.Count} rider{(riders.Count == 1 ? "" : "s")}.",
                NotificationTypes.Info, RideShareService.RideUrl(post.Id), NotificationCategories.Ride, relatedId: post.Id);
            foreach (var rider in riders)
            {
                notifications.Notify(rider, "Your ride leaves soon", $"The ride from {post.Source} to {post.Destination} leaves at {when}. Check the pickup details.",
                    NotificationTypes.Info, RideShareService.RideUrl(post.Id), NotificationCategories.Ride, post.UserId, post.Id);
            }
            post.ReminderSentAt = now;
        }
        await db.SaveChangesAsync(ct);
        return due.Count;
    }

    private async Task<int> ExpireAsync(DateTime now, CancellationToken ct)
    {
        var graceLimit = now - CompletionGrace;
        var due = await db.RidePosts.Include(p => p.Requests)
            .Where(p => p.DeletedAt == null && (p.Status == RideStatus.Active || p.Status == RideStatus.Full) && p.DepartureAt != null
                        && (p.DepartureAt <= graceLimit
                            || (p.DepartureAt <= now && !p.Requests.Any(r => r.Status == RideRequestStatus.Accepted))))
            .ToListAsync(ct);
        var count = 0;
        foreach (var candidate in due)
        {
            // Only change a ride that is still live; a host completing or cancelling it right now wins.
            var changed = await db.RidePosts
                .Where(p => p.Id == candidate.Id && (p.Status == RideStatus.Active || p.Status == RideStatus.Full))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, RideStatus.Expired).SetProperty(p => p.ExpiredAt, now).SetProperty(p => p.UpdatedAt, now), ct);
            if (changed == 0)
            {
                continue;
            }
            count++;
            await db.RideRequests.Where(r => r.RidePostId == candidate.Id && r.Status == RideRequestStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RideRequestStatus.Cancelled).SetProperty(r => r.UpdatedAt, now), ct);
            notifications.Notify(candidate.UserId, "Ride expired",
                $"Your ride from {candidate.Source} to {candidate.Destination} expired because its departure time passed{(candidate.Requests.Any(r => r.Status == RideRequestStatus.Accepted) ? " and it was not marked as completed" : "")}.",
                NotificationTypes.Info, RideShareService.RideUrl(candidate.Id), NotificationCategories.Ride, relatedId: candidate.Id);
        }
        await db.SaveChangesAsync(ct);
        return count;
    }
}

// Runs RideLifecycle every minute (RideLifecycle:IntervalSeconds) while the server is up. Turned off with
// RideLifecycle:Enabled = false (the integration tests run it by hand instead).
public class RideLifecycleHostedService(IServiceScopeFactory scopes, IConfiguration config, ILogger<RideLifecycleHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("RideLifecycle:Enabled", true))
        {
            return;
        }
        var interval = TimeSpan.FromSeconds(Math.Max(15, config.GetValue("RideLifecycle:IntervalSeconds", 60)));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<RideLifecycle>().RunOnceAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Ride lifecycle check failed; it will try again.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
