using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// F20: route and stop suggestions. Everything needs a sign-in; the submitter is always the one in the token (a
// userId or status in the body is ignored). Users see and delete only their own suggestions; listing all of them,
// previewing an approval and reviewing are for admins. The rules live in RouteSuggestionService.
[Authorize]
[Route("api/route-suggestions")]
public class RouteSuggestionController(RouteSuggestionService suggestions) : ApiControllerBase
{
    private RouteSuggestionViewModel View(RouteSuggestion s) => RouteSuggestionViewModel.From(s, CurrentUserId, IsAdmin);

    [HttpGet("options")]
    public IActionResult Options() => Ok(new
    {
        success = true,
        types = RouteSuggestionTypes.All,
        statuses = RouteSuggestionStatus.All,
        correctionFields = RouteCorrectionFields.All,
        busTypes = RouteBusTypes.All,
        maxPending = RouteSuggestionService.MaxPendingPerUser,
    });

    [HttpPost]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> Create(CreateRouteSuggestionRequest request, CancellationToken ct)
    {
        var result = await suggestions.CreateAsync(CurrentUserId, request, ct);
        return result.Succeeded
            ? Created201(new { success = true, message = result.Message, data = View(result.Value!) })
            : Error(result.StatusCode, result.Message);
    }

    // The owner changes their own suggestion while it is still pending. The body is the same as for sending.
    [HttpPut("{id:guid}")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> Update(Guid id, CreateRouteSuggestionRequest request, CancellationToken ct)
    {
        var result = await suggestions.UpdateAsync(id, CurrentUserId, request, request.RemoveImage, ct);
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, data = View(result.Value!) })
            : Error(result.StatusCode, result.Message);
    }

    // The caller's own suggestions, newest first. ?page · pageSize.
    [HttpGet("my")]
    public async Task<IActionResult> My([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var (items, total) = await suggestions.MineAsync(CurrentUserId, page, pageSize, ct);
        return Ok(new { success = true, page, pageSize, total, totalPages = (int)Math.Ceiling(total / (double)pageSize), data = items.Select(View) });
    }

    // ?status=PENDING|APPROVED|REJECTED · type=NEW_ROUTE|NEW_STOP|INCORRECT_INFORMATION · search (RS-code, title or user) · page · pageSize.
    [Authorize(Roles = UserRoles.Admin)]
    [HttpGet("all")]
    public async Task<IActionResult> All([FromQuery] string? status, [FromQuery] string? type, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 15, CancellationToken ct = default)
    {
        status = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();
        if (status is not null && !RouteSuggestionStatus.All.Contains(status))
        {
            return BadRequestError("Unknown status.");
        }
        type = string.IsNullOrWhiteSpace(type) ? null : type.Trim().ToUpperInvariant();
        if (type is not null && !RouteSuggestionTypes.All.Contains(type))
        {
            return BadRequestError("Unknown suggestion type.");
        }
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var (items, total) = await suggestions.AllAsync(new RouteSuggestionFilter(status, type, search?.Length > 100 ? search[..100] : search, page, pageSize), ct);
        var counts = await suggestions.CountsAsync(ct);
        return Ok(new
        {
            success = true, page, pageSize, total, totalPages = (int)Math.Ceiling(total / (double)pageSize),
            counts = new { pending = counts.Pending, approved = counts.Approved, rejected = counts.Rejected },
            data = items.Select(View),
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var suggestion = await suggestions.FindAsync(id, CurrentUserId, IsAdmin, ct);
        return suggestion is null ? NotFoundError("Suggestion not found.") : Ok(new { success = true, data = View(suggestion) });
    }

    [HttpGet("{id:guid}/image")]
    public async Task<IActionResult> Image(Guid id, CancellationToken ct)
    {
        var stored = await suggestions.ImageAsync(id, CurrentUserId, IsAdmin, ct);
        if (stored is null)
        {
            return NotFoundError("Picture not found.");
        }
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(stored.Content, stored.ContentType);
    }

    // What approving would add to or change in the official bus data, for the admin's confirmation.
    [Authorize(Roles = UserRoles.Admin)]
    [HttpGet("{id:guid}/preview")]
    public async Task<IActionResult> Preview(Guid id, CancellationToken ct)
    {
        var result = await suggestions.PreviewAsync(id, CurrentUserId, ct);
        return result.Succeeded ? Ok(new { success = true, data = result.Value }) : Error(result.StatusCode, result.Message);
    }

    [Authorize(Roles = UserRoles.Admin)]
    [HttpPut("{id:guid}/review")]
    public async Task<IActionResult> Review(Guid id, ReviewRouteSuggestionRequest request, CancellationToken ct)
    {
        var result = await suggestions.ReviewAsync(id, CurrentUserId, request, ct);
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, data = View(result.Value!) })
            : Error(result.StatusCode, result.Message);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await suggestions.DeleteAsync(id, CurrentUserId, IsAdmin, ct);
        return result.Succeeded ? Ok(new { success = true, message = result.Message }) : Error(result.StatusCode, result.Message);
    }
}
