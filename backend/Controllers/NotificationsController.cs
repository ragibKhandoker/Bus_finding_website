using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// The signed-in user's notifications, and SOS alerts. Every call needs a token; a {userId} in the route must be the
// signed-in user, and someone else's notification is reported as not found.
[Authorize]
[Route("api/notifications")]
public class NotificationsController(NotificationService notifications) : ApiControllerBase
{
    // Newest first: ?page=1&pageSize=20 (pageSize at most 100; limit is the older name for pageSize), ?unreadOnly=true.
    [HttpGet("user/{userId:guid}")]
    public async Task<IActionResult> ForUser(Guid userId, [FromQuery] int page = 1, [FromQuery] int? pageSize = null,
        [FromQuery] int? limit = null, [FromQuery] bool unreadOnly = false)
    {
        if (NotMe(userId) is { } refused)
        {
            return refused;
        }
        page = Math.Max(1, page);
        var size = Math.Clamp(pageSize ?? limit ?? 20, 1, 100);
        var (items, total) = await notifications.ListAsync(userId, page, size, unreadOnly);
        return Ok(new
        {
            success = true,
            count = items.Count,
            total,
            page,
            pageSize = size,
            totalPages = (int)Math.Ceiling(total / (double)size),
            unreadCount = await notifications.UnreadCountAsync(userId),
            data = items.Select(NotificationViewModel.From),
        });
    }

    [HttpGet("unread/{userId:guid}")]
    public async Task<IActionResult> UnreadCount(Guid userId) =>
        NotMe(userId) ?? Ok(new { success = true, count = await notifications.UnreadCountAsync(userId) });

    [HttpPatch("read/{notificationId:guid}")]
    public async Task<IActionResult> MarkRead(Guid notificationId)
    {
        var result = await notifications.MarkReadAsync(CurrentUserId, notificationId);
        return result.Succeeded
            ? Ok(new { success = true, data = NotificationViewModel.From(result.Value!), unreadCount = await notifications.UnreadCountAsync(CurrentUserId) })
            : Error(result.StatusCode, result.Message);
    }

    // PATCH read-all/{userId}; the older PATCH read-all (with { userId } in the body) does the same for the signed-in user.
    [HttpPatch("read-all/{userId:guid}")]
    public async Task<IActionResult> MarkAllRead(Guid userId) =>
        NotMe(userId) ?? Ok(new { success = true, message = "Notifications marked as read.", count = await notifications.MarkAllReadAsync(userId), unreadCount = 0 });

    [HttpPatch("read-all")]
    public Task<IActionResult> MarkAllReadForMe() => MarkAllRead(CurrentUserId);

    [HttpDelete("{notificationId:guid}")]
    public async Task<IActionResult> Delete(Guid notificationId)
    {
        var result = await notifications.DeleteAsync(CurrentUserId, notificationId);
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, unreadCount = await notifications.UnreadCountAsync(CurrentUserId) })
            : Error(result.StatusCode, result.Message);
    }

    // DELETE clear/{userId}; DELETE user/{userId} is the older path. Only the signed-in user's own notifications go.
    [HttpDelete("clear/{userId:guid}")]
    [HttpDelete("user/{userId:guid}")]
    public async Task<IActionResult> Clear(Guid userId) =>
        NotMe(userId) ?? Ok(new { success = true, message = "Notifications cleared.", count = await notifications.ClearAsync(userId), unreadCount = 0 });

    // Sends an emergency alert with a Google Maps link to the sender's friends and ride companions (see
    // NotificationService.SosRecipientsAsync). It is an in-app alert only; no emergency service is contacted.
    [EnableRateLimiting("strict")]
    [HttpPost("sos")]
    public async Task<IActionResult> Sos(SosRequest request)
    {
        var result = await notifications.SendSosAsync(CurrentUserId, request.Latitude, request.Longitude, request.ServiceName, request.Message);
        return result.Succeeded
            ? Created201(new { success = true, message = result.Message, count = result.Value })
            : Error(result.StatusCode, result.Message);
    }

    private ObjectResult? NotMe(Guid userId) => userId == CurrentUserId ? null : ForbiddenError("You can only see your own notifications.");
}
