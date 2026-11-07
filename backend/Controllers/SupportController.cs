using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// The signed-in user's conversation with the admins. Admins answer from /admin/support instead.
[Authorize]
[Route("support")]
public class SupportController(SupportService support) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Thread()
    {
        if (IsAdmin)
        {
            return AdminsUseInbox();
        }
        var messages = await support.ThreadAsync(CurrentUserId);
        var unread = await support.UnreadForUserAsync(CurrentUserId);
        return Ok(new { success = true, unread, messages = messages.Select(SupportMessageViewModel.From) });
    }

    [HttpGet("unread")]
    public async Task<IActionResult> Unread() =>
        Ok(new { success = true, count = IsAdmin ? 0 : await support.UnreadForUserAsync(CurrentUserId) });

    [EnableRateLimiting("chat-send")]
    [HttpPost("messages")]
    public async Task<IActionResult> Send(SupportMessageRequest request)
    {
        if (IsAdmin)
        {
            return AdminsUseInbox();
        }
        var result = await support.SendAsync(CurrentUserId, CurrentUserId, fromAdmin: false, request.Text, request.Emergency);
        return result.Succeeded
            ? Created201(new { success = true, message = SupportMessageViewModel.From(result.Value!) })
            : Error(result.StatusCode, result.Message);
    }

    [HttpPost("read")]
    public async Task<IActionResult> MarkRead() =>
        Ok(new { success = true, marked = IsAdmin ? 0 : await support.MarkReadAsync(CurrentUserId, readerIsAdmin: false) });

    private ObjectResult AdminsUseInbox() => BadRequestError("Admins answer messages from the Admin page.");
}
