using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// Called without a token by the existing clients; see CanActAs for how a token is honoured when present.
[Route("api/notifications")]
public class NotificationsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet("user/{userId:guid}")]
    public async Task<IActionResult> ForUser(Guid userId, [FromQuery] int limit = 50, [FromQuery] bool unreadOnly = false)
    {
        if (!CanActAs(userId))
        {
            return ForbiddenError();
        }

        var query = db.Notifications.Where(n => n.UserId == userId);
        if (unreadOnly)
        {
            query = query.Where(n => !n.IsRead);
        }
        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .AsNoTracking()
            .ToListAsync();
        return Ok(new { success = true, count = items.Count, data = items.Select(NotificationViewModel.From) });
    }

    [HttpGet("unread/{userId:guid}")]
    public async Task<IActionResult> UnreadCount(Guid userId)
    {
        if (!CanActAs(userId))
        {
            return ForbiddenError();
        }
        var count = await db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);
        return Ok(new { success = true, count });
    }

    [HttpPatch("read/{notificationId:guid}")]
    public async Task<IActionResult> MarkRead(Guid notificationId)
    {
        var notification = await db.Notifications.FindAsync(notificationId);
        if (notification is null)
        {
            return NotFoundError("Notification not found.");
        }
        if (!CanActAs(notification.UserId))
        {
            return ForbiddenError();
        }

        notification.IsRead = true;
        await db.SaveChangesAsync();
        return Ok(new { success = true, data = NotificationViewModel.From(notification) });
    }

    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllRead(UserIdRequest request)
    {
        var userId = request.UserId!.Value;
        if (!CanActAs(userId))
        {
            return ForbiddenError();
        }
        var count = await db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
        return Ok(new { success = true, count });
    }

    [HttpDelete("{notificationId:guid}")]
    public async Task<IActionResult> Delete(Guid notificationId)
    {
        var notification = await db.Notifications.FindAsync(notificationId);
        if (notification is null)
        {
            return NotFoundError("Notification not found.");
        }
        if (!CanActAs(notification.UserId))
        {
            return ForbiddenError();
        }

        db.Notifications.Remove(notification);
        await db.SaveChangesAsync();
        return Ok(new { success = true, message = "Notification deleted." });
    }

    [HttpDelete("user/{userId:guid}")]
    public async Task<IActionResult> DeleteAll(Guid userId)
    {
        if (!CanActAs(userId))
        {
            return ForbiddenError();
        }
        var count = await db.Notifications.Where(n => n.UserId == userId).ExecuteDeleteAsync();
        return Ok(new { success = true, count });
    }

    // Sends an emergency alert with the sender's location to every other user.
    [EnableRateLimiting("strict")]
    [HttpPost("sos")]
    public async Task<IActionResult> Sos(SosRequest request)
    {
        var sender = await db.Users.FindAsync(request.UserId);
        if (sender is null)
        {
            return NotFoundError("User not found.");
        }
        if (!CanActAs(sender.Id))
        {
            return ForbiddenError();
        }

        var service = string.IsNullOrWhiteSpace(request.ServiceName) ? "Emergency" : request.ServiceName.Trim();
        var name = string.IsNullOrWhiteSpace(request.UserName) ? sender.Name : request.UserName.Trim();
        var where = string.IsNullOrWhiteSpace(request.Source) ? "an unknown location" : request.Source.Trim();
        string? mapUrl = null;
        if (request.Latitude is { } lat && request.Longitude is { } lon)
        {
            mapUrl = FormattableString.Invariant($"https://www.openstreetmap.org/?mlat={lat}&mlon={lon}#map=17/{lat}/{lon}");
            where += FormattableString.Invariant($" ({lat:F5}, {lon:F5})");
        }

        var recipients = await db.Users.Where(u => u.Id != sender.Id).Select(u => u.Id).ToListAsync();
        db.Notifications.AddRange(recipients.Select(id => new Notification
        {
            UserId = id,
            Title = $"🚨 {service} alert",
            Message = $"{name} needs {service.ToLowerInvariant()} help near {where}.",
            Type = NotificationTypes.Sos,
            ActionUrl = mapUrl,
        }));
        await db.SaveChangesAsync();

        return Created201(new { success = true, message = $"{service} alert sent to {recipients.Count} users.", count = recipients.Count });
    }
}
