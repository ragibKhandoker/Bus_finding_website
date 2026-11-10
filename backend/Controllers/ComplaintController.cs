using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// F18: complaints about Dhaka buses. Everything needs a sign-in; the user is always the one in the token. Users see
// and delete only their own complaints; listing all of them, the statistics and status changes are for admins.
[Authorize]
[Route("api/complaints")]
public class ComplaintController(ComplaintService complaints) : ApiControllerBase
{
    private ComplaintViewModel View(Complaint c, bool details = false) => ComplaintViewModel.From(c, CurrentUserId, IsAdmin, details);

    [HttpGet("options")]
    public IActionResult Options() => Ok(new { success = true, categories = ComplaintCategories.All, statuses = ComplaintStatus.All });

    [EnableRateLimiting("strict")]
    [HttpPost("create")]
    [RequestSizeLimit(24 * 1024 * 1024)]
    public async Task<IActionResult> Create(CreateComplaintRequest request)
    {
        var result = await complaints.CreateAsync(CurrentUserId, request);
        return result.Succeeded
            ? StatusCode(result.StatusCode, new { success = true, message = result.Message, data = View(result.Value!, true) })
            : Error(result.StatusCode, result.Message);
    }

    [HttpGet("my")]
    public async Task<IActionResult> My() => Ok(new { success = true, data = (await complaints.MineAsync(CurrentUserId)).Select(c => View(c)) });

    // ?search=CMP-3F2A or a bus, subject or user · category · status · from/to (incident date, yyyy-MM-dd) ·
    // sort=newest|oldest · page · pageSize.
    [Authorize(Roles = UserRoles.Admin)]
    [HttpGet("all")]
    public async Task<IActionResult> All([FromQuery] string? search, [FromQuery] string? category, [FromQuery] string? status, [FromQuery] string? from,
        [FromQuery] string? to, [FromQuery] string? sort, [FromQuery] int page = 1, [FromQuery] int pageSize = 15)
    {
        status = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();
        if (status is not null && !ComplaintStatus.All.Contains(status))
        {
            return BadRequestError("Unknown status.");
        }
        DateOnly? Date(string? text) => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var (items, total) = await complaints.AllAsync(new ComplaintFilter(search?.Length > 100 ? search[..100] : search,
            string.IsNullOrWhiteSpace(category) ? null : category.Trim(), status, Date(from), Date(to), sort == "oldest", page, pageSize));
        return Ok(new { success = true, page, pageSize, total, totalPages = (int)Math.Ceiling(total / (double)pageSize), data = items.Select(c => View(c)) });
    }

    [Authorize(Roles = UserRoles.Admin)]
    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var s = await complaints.StatsAsync(DateTime.UtcNow);
        return Ok(new
        {
            success = true,
            data = new
            {
                s.Total, s.Pending, s.Reviewing, s.Resolved, s.Rejected,
                byCategory = s.ByCategory.Select(x => new { category = x.Category, count = x.Count }),
                last30Days = s.Last30Days.Select(x => new { date = x.Date, count = x.Count }),
                topBuses = s.TopBuses.Select(x => new { bus = x.Bus, count = x.Count }),
            },
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var complaint = await complaints.FindAsync(id, CurrentUserId, IsAdmin);
        return complaint is null ? NotFoundError("Complaint not found.") : Ok(new { success = true, data = View(complaint, true) });
    }

    [HttpGet("{id:guid}/history")]
    public async Task<IActionResult> History(Guid id)
    {
        var history = await complaints.HistoryAsync(id, CurrentUserId, IsAdmin);
        return history is null ? NotFoundError("Complaint not found.") : Ok(new { success = true, data = history.Select(h => ComplaintHistoryViewModel.From(h, IsAdmin)) });
    }

    // Only the owner of the picture's complaint and admins; never cached by shared caches.
    [HttpGet("{id:guid}/evidence/{evidenceId:guid}")]
    public async Task<IActionResult> Evidence(Guid id, Guid evidenceId)
    {
        var stored = await complaints.EvidenceAsync(id, evidenceId, CurrentUserId, IsAdmin);
        if (stored is null)
        {
            return NotFoundError("Picture not found.");
        }
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(stored.Content, stored.ContentType);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await complaints.DeleteAsync(CurrentUserId, id);
        return result.Succeeded ? Ok(new { success = true, message = result.Message }) : Error(result.StatusCode, result.Message);
    }

    // { "status": "RESOLVED", "adminNotes": "…" }
    [Authorize(Roles = UserRoles.Admin)]
    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, ComplaintStatusRequest request)
    {
        var result = await complaints.SetStatusAsync(CurrentUserId, id, request.Status, request.AdminNotes);
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, data = View(result.Value!, true) })
            : Error(result.StatusCode, result.Message);
    }
}
