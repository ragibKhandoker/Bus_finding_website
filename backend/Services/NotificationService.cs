using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

public record NotificationResult<T>(int StatusCode, string Message, T? Value = default)
{
    public bool Succeeded => StatusCode is >= 200 and < 300;
}

// In-app notifications. Notify queues one on the current DbContext and the caller's SaveChangesAsync persists it,
// so a notification is only stored when the change it describes is. The other methods act for one user: the
// signed-in user, passed in from the token. Another user's notification is reported as not found.
public class NotificationService(AppDbContext db)
{
    public void Notify(Guid userId, string title, string message, string type = NotificationTypes.Info, string? actionUrl = null,
        string category = NotificationCategories.General, Guid? senderId = null, Guid? relatedId = null)
    {
        db.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = title,
            Message = message,
            Type = type,
            ActionUrl = actionUrl,
            Category = category,
            SenderId = senderId,
            RelatedId = relatedId,
        });
    }

    public async Task<(List<Notification> Items, int Total)> ListAsync(Guid userId, int page, int pageSize, bool unreadOnly)
    {
        var query = db.Notifications.Where(n => n.UserId == userId && (!unreadOnly || !n.IsRead));
        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .AsNoTracking()
            .ToListAsync();
        return (items, total);
    }

    public Task<int> UnreadCountAsync(Guid userId) => db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);

    public async Task<NotificationResult<Notification>> MarkReadAsync(Guid userId, Guid notificationId)
    {
        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);
        if (notification is null)
        {
            return new(StatusCodes.Status404NotFound, "Notification not found.");
        }
        notification.IsRead = true;
        await db.SaveChangesAsync();
        return new(StatusCodes.Status200OK, "", notification);
    }

    public Task<int> MarkAllReadAsync(Guid userId) => db.Notifications
        .Where(n => n.UserId == userId && !n.IsRead)
        .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));

    public async Task<NotificationResult<bool>> DeleteAsync(Guid userId, Guid notificationId)
    {
        var deleted = await db.Notifications.Where(n => n.Id == notificationId && n.UserId == userId).ExecuteDeleteAsync();
        return deleted == 0
            ? new(StatusCodes.Status404NotFound, "Notification not found.")
            : new(StatusCodes.Status200OK, "Notification deleted.", true);
    }

    public Task<int> ClearAsync(Guid userId) => db.Notifications.Where(n => n.UserId == userId).ExecuteDeleteAsync();

    // The people an SOS from this user reaches: their confirmed friends, and everyone on an open ride share with
    // them (the host and accepted riders of rides they host or have joined). Never anyone else.
    public async Task<List<Guid>> SosRecipientsAsync(Guid senderId)
    {
        var friends = await db.Friendships
            .Where(f => f.User1Id == senderId || f.User2Id == senderId)
            .Select(f => f.User1Id == senderId ? f.User2Id : f.User1Id)
            .ToListAsync();
        var openRides = db.RidePosts.Where(p => p.Status == RideStatus.Active && p.DeletedAt == null
            && (p.UserId == senderId || p.Requests.Any(r => r.RequesterId == senderId && r.Status == RideRequestStatus.Accepted)));
        var hosts = await openRides.Select(p => p.UserId).ToListAsync();
        var riders = await openRides.SelectMany(p => p.Requests).Where(r => r.Status == RideRequestStatus.Accepted).Select(r => r.RequesterId).ToListAsync();
        return friends.Concat(hosts).Concat(riders).Where(id => id != senderId).Distinct().ToList();
    }

    public static string MapLink(double latitude, double longitude) =>
        "https://www.google.com/maps?q=" + string.Create(CultureInfo.InvariantCulture, $"{latitude:0.######},{longitude:0.######}");

    // Sends an emergency alert with a Google Maps link to the sender's location to their SOS recipients. The location
    // goes only into those recipients' notifications and is not logged. Value is how many people were alerted.
    public async Task<NotificationResult<int>> SendSosAsync(Guid senderId, double? latitude, double? longitude, string? kind, string? note)
    {
        if (latitude is not { } lat || longitude is not { } lon || !double.IsFinite(lat) || !double.IsFinite(lon)
            || lat is < -90 or > 90 || lon is < -180 or > 180)
        {
            return new(StatusCodes.Status400BadRequest, "Your location is needed to send an SOS. Allow location access and try again.");
        }
        var sender = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == senderId);
        if (sender is null)
        {
            return new(StatusCodes.Status401Unauthorized, "Please sign in to continue.");
        }
        var recipients = await SosRecipientsAsync(senderId);
        if (recipients.Count == 0)
        {
            return new(StatusCodes.Status409Conflict,
                "No alert was sent: SOS goes to your friends and the people on your open ride shares, and you have none yet. Add friends first, and call 999 in an emergency.");
        }

        var help = string.IsNullOrWhiteSpace(kind) ? "help" : $"{kind.Trim().ToLowerInvariant()} help";
        var message = $"{sender.Name} needs {help} and shared their location. Open the map to see where they are.";
        if (!string.IsNullOrWhiteSpace(note))
        {
            message += $" They wrote: \"{note.Trim()}\"";
        }
        var link = MapLink(lat, lon);
        foreach (var id in recipients)
        {
            Notify(id, $"🚨 SOS from {sender.Name}", message, NotificationTypes.Sos, link, NotificationCategories.Sos, senderId);
        }
        await db.SaveChangesAsync();
        return new(StatusCodes.Status201Created, recipients.Count == 1 ? "SOS sent to 1 person." : $"SOS sent to {recipients.Count} people.", recipients.Count);
    }
}
