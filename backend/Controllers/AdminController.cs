using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// Account management for admins: find users, approve forgotten-password requests and message users.
// Admins never see any password: passwords exist only as bcrypt hashes. The rules live in AdminService, PasswordResetService and SupportService.
[Authorize(Roles = UserRoles.Admin)]
[Route("admin")]
public class AdminController(
    AdminService admin,
    PasswordResetService passwordResets,
    SupportService support) : ApiControllerBase
{
    // Counts for the admin dashboard and profile: who uses the app and what is waiting for an admin.
    [HttpGet("overview")]
    public async Task<IActionResult> Overview() => Ok(new { success = true, data = await admin.OverviewAsync() });

    [HttpGet("users")]
    public async Task<IActionResult> Users([FromQuery] string? search, [FromQuery] int limit = 50)
    {
        var users = await admin.UsersAsync(search, limit);
        return Ok(new { success = true, count = users.Count, data = users.Select(AdminUserViewModel.From) });
    }

    // ---- Forgot-password requests ----

    // status: pending (default), sent, completed, dismissed or all.
    [HttpGet("password-requests")]
    public async Task<IActionResult> PasswordRequests([FromQuery] string? status = PasswordResetStatuses.Pending)
    {
        var requests = await passwordResets.ListAsync(status);
        var pending = await passwordResets.PendingCountAsync();
        return Ok(new { success = true, pending, data = requests.Select(AdminResetRequestViewModel.From) });
    }

    // Approves a request. Admins never see a password: the user collects their temporary password from the
    // sign-in form, where it is made and shown once. ("send" is the older name of the same action.)
    [HttpPost("password-requests/{requestId:guid}/approve")]
    [HttpPost("password-requests/{requestId:guid}/send")]
    public async Task<IActionResult> ApprovePasswordRequest(Guid requestId)
    {
        var result = await passwordResets.ApproveAsync(requestId, CurrentUserId);
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, request = AdminResetRequestViewModel.From(result.Value!) })
            : Error(result.StatusCode, result.Message);
    }

    [HttpPost("password-requests/{requestId:guid}/dismiss")]
    public async Task<IActionResult> DismissPasswordRequest(Guid requestId, DismissResetRequest? request)
    {
        var result = await passwordResets.DismissAsync(requestId, CurrentUserId, request?.Reason);
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, request = AdminResetRequestViewModel.From(result.Value!) })
            : Error(result.StatusCode, result.Message);
    }

    // ---- Messages with users ----

    [HttpGet("support")]
    public async Task<IActionResult> SupportThreads()
    {
        var threads = await support.ThreadsAsync();
        return Ok(new
        {
            success = true,
            unread = threads.Sum(t => t.Unread),
            emergencyUnread = threads.Sum(t => t.EmergencyUnread),
            data = threads.Select(SupportThreadViewModel.From),
        });
    }

    [HttpGet("support/{userId:guid}")]
    public async Task<IActionResult> SupportThread(Guid userId)
    {
        var user = await support.ThreadOwnerAsync(userId);
        if (user is null)
        {
            return NotFoundError("User not found.");
        }
        var messages = await support.ThreadAsync(userId);
        return Ok(new { success = true, user = AdminUserViewModel.From(user), messages = messages.Select(SupportMessageViewModel.From) });
    }

    // Writes to a user, starting the conversation if there is none yet.
    [EnableRateLimiting("chat-send")]
    [HttpPost("support/{userId:guid}")]
    public async Task<IActionResult> SendSupportMessage(Guid userId, SupportMessageRequest request)
    {
        var result = await support.SendAsync(userId, CurrentUserId, fromAdmin: true, request.Text, request.Emergency);
        return result.Succeeded
            ? Created201(new { success = true, message = SupportMessageViewModel.From(result.Value!) })
            : Error(result.StatusCode, result.Message);
    }

    [HttpPost("support/{userId:guid}/read")]
    public async Task<IActionResult> MarkSupportRead(Guid userId) =>
        Ok(new { success = true, marked = await support.MarkReadAsync(userId, readerIsAdmin: true) });
}
