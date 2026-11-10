using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// F17: Lost & Found reports on Dhaka buses. Everything needs a sign-in (reports carry contact details); the user is
// always the one in the token. The rules live in LostFoundService.
[Authorize]
[Route("api/lostfound")]
public class LostFoundController(LostFoundService reports) : ApiControllerBase
{
    private LostFoundViewModel View(LostFound r) => LostFoundViewModel.From(r, CurrentUserId, IsAdmin);

    private IActionResult Answer(ServiceResult<LostFound> result) => result.Succeeded
        ? StatusCode(result.StatusCode, new { success = true, message = result.Message, data = View(result.Value!) })
        : Error(result.StatusCode, result.Message);

    // The form's choices.
    [HttpGet("options")]
    public IActionResult Options() => Ok(new
    {
        success = true,
        types = LostFoundTypes.All,
        categories = LostFoundCategories.All,
        statuses = LostFoundStatus.All,
        ownerStatuses = LostFoundStatus.ForOwner,
    });

    // ?type=lost|found&category=Wallet&search=…&status=open&page=1&pageSize=12. Closed reports are hidden unless
    // asked for.
    [HttpGet("all")]
    public async Task<IActionResult> All([FromQuery] string? type, [FromQuery] string? category, [FromQuery] string? search, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 12)
    {
        type = string.IsNullOrWhiteSpace(type) ? null : type.Trim().ToLowerInvariant();
        status = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant();
        if (type is not null && !LostFoundTypes.All.Contains(type))
        {
            return BadRequestError("Type must be lost or found.");
        }
        if (status is not null && !LostFoundStatus.All.Contains(status))
        {
            return BadRequestError("Unknown status.");
        }
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var (items, total) = await reports.ListAsync(new LostFoundFilter(type, string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
            search?.Length > 100 ? search[..100] : search, status, page, pageSize));
        return Ok(new { success = true, page, pageSize, total, totalPages = (int)Math.Ceiling(total / (double)pageSize), data = items.Select(View) });
    }

    [HttpGet("my")]
    public async Task<IActionResult> My() => Ok(new { success = true, data = (await reports.MineAsync(CurrentUserId)).Select(View) });

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var report = await reports.FindAsync(id);
        if (report is null)
        {
            return NotFoundError("Report not found.");
        }
        return Ok(new { success = true, data = View(report), matches = (await reports.MatchesAsync(report)).Select(View) });
    }

    [EnableRateLimiting("strict")]
    [HttpPost("create")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> Create(LostFoundRequest request) => Answer(await reports.CreateAsync(CurrentUserId, request));

    [HttpPut("{id:guid}")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> Update(Guid id, LostFoundRequest request) => Answer(await reports.UpdateAsync(CurrentUserId, IsAdmin, id, request));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await reports.DeleteAsync(CurrentUserId, IsAdmin, id);
        return result.Succeeded ? Ok(new { success = true, message = result.Message }) : Error(result.StatusCode, result.Message);
    }

    // Admins: { "status": "under-review", "note": "…" }.
    [Authorize(Roles = UserRoles.Admin)]
    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, LostFoundStatusRequest request) => Answer(await reports.SetStatusAsync(CurrentUserId, id, request.Status, request.Note));

    // ---- Messages: a private conversation between a report's author and one other user ----

    // Starts (or continues) a conversation about a report with a first message: { "text": "I think I found it…" }.
    [EnableRateLimiting("chat-send")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    [HttpPost("{id:guid}/conversations")]
    public async Task<IActionResult> StartConversation(Guid id, LostFoundMessageRequest request, [FromServices] LostFoundChatService chats)
    {
        var result = await chats.StartAsync(CurrentUserId, id, request.Text, request.Image);
        return result.Succeeded
            ? StatusCode(result.StatusCode, new { success = true, message = result.Message, data = LostFoundConversationViewModel.From(result.Value!, CurrentUserId) })
            : Error(result.StatusCode, result.Message);
    }

    [HttpGet("conversations")]
    public async Task<IActionResult> Conversations([FromServices] LostFoundChatService chats)
    {
        var list = await chats.ListAsync(CurrentUserId);
        return Ok(new { success = true, unread = list.Sum(x => x.Unread), data = list.Select(x => LostFoundConversationViewModel.From(x.Conversation, CurrentUserId, x.Unread, x.Last)) });
    }

    // ?after=2026-11-10T08:00:00Z returns only newer messages (the page asks every few seconds). Reading marks them read.
    [HttpGet("conversations/{conversationId:guid}/messages")]
    public async Task<IActionResult> Messages(Guid conversationId, [FromQuery] DateTime? after, [FromServices] LostFoundChatService chats)
    {
        var result = await chats.MessagesAsync(CurrentUserId, conversationId, after?.ToUniversalTime());
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        var (conversation, messages, reactions) = result.Value;
        return Ok(new
        {
            success = true,
            conversation = LostFoundConversationViewModel.From(conversation, CurrentUserId),
            data = messages.Select(m => LostFoundMessageViewModel.From(m, CurrentUserId, reactions.Where(r => r.MessageId == m.Id))),
            // Every message's reactions, so the page can update reactions on older messages too.
            reactions = reactions.GroupBy(r => r.MessageId).ToDictionary(g => g.Key.ToString(), g => LostFoundReactionViewModel.Summary(g, CurrentUserId)),
        });
    }

    [EnableRateLimiting("chat-send")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    [HttpPost("conversations/{conversationId:guid}/messages")]
    public async Task<IActionResult> Send(Guid conversationId, LostFoundMessageRequest request, [FromServices] LostFoundChatService chats)
    {
        var result = await chats.SendAsync(CurrentUserId, conversationId, request.Text, request.Image);
        return result.Succeeded
            ? StatusCode(result.StatusCode, new { success = true, message = result.Message, data = LostFoundMessageViewModel.From(result.Value!, CurrentUserId) })
            : Error(result.StatusCode, result.Message);
    }

    // { "emoji": "👍" } sets or changes this user's reaction; the same emoji again, or an empty one, removes it.
    [EnableRateLimiting("chat-send")]
    [HttpPut("conversations/{conversationId:guid}/messages/{messageId:guid}/reaction")]
    public async Task<IActionResult> React(Guid conversationId, Guid messageId, LostFoundReactionRequest request, [FromServices] LostFoundChatService chats)
    {
        var result = await chats.ReactAsync(CurrentUserId, conversationId, messageId, request.Emoji);
        return result.Succeeded
            ? Ok(new { success = true, data = LostFoundReactionViewModel.Summary(result.Value!, CurrentUserId) })
            : Error(result.StatusCode, result.Message);
    }

    // A report's picture. The file name is random and only given to signed-in users with the report, so the
    // picture can be shown with a plain <img>.
    [AllowAnonymous]
    [HttpGet("images/{key}")]
    public IActionResult Image(string key)
    {
        var stored = reports.OpenImage(key);
        if (stored is null)
        {
            return NotFoundError("Picture not found.");
        }
        Response.Headers.CacheControl = "private, max-age=86400";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
        return File(stored.Content, stored.ContentType);
    }
}
