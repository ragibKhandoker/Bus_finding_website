using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

// Queues a notification on the current DbContext; the caller's SaveChangesAsync persists it.
public class NotificationService(AppDbContext db)
{
    public void Notify(Guid userId, string title, string message, string type = NotificationTypes.Info, string? actionUrl = null)
    {
        db.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = title,
            Message = message,
            Type = type,
            ActionUrl = actionUrl,
        });
    }
}
