using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// F21: the admin's Driver Verification Dashboard. Admins only. {driverId} is the driver's user id. Licence and NID
// numbers are always masked; document images are streamed only from here, never cached by shared caches.
[Authorize(Roles = UserRoles.Admin)]
[Route("api/admin/drivers")]
public class AdminDriverController(DriverVerificationService verification) : ApiControllerBase
{
    private static DateOnly Today => DriverRules.Today(DateTime.UtcNow);

    // ?search=name or email · status · from/to (submission date, yyyy-MM-dd) · sort=newest|oldest · page · pageSize
    [HttpGet("")]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] string? status, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] string? sort, [FromQuery] int page = 1, [FromQuery] int pageSize = 15)
    {
        status = string.IsNullOrWhiteSpace(status) || status.Equals("all", StringComparison.OrdinalIgnoreCase) ? null : status.Trim().ToUpperInvariant();
        if (status is not null && !DriverVerificationStatus.All.Contains(status))
        {
            return BadRequestError("Unknown status.");
        }
        DateOnly? Date(string? text) => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var (items, total) = await verification.ListAsync(new DriverApplicationFilter(search?.Length > 100 ? search[..100] : search, status,
            Date(from), Date(to), string.Equals(sort, "oldest", StringComparison.OrdinalIgnoreCase), page, pageSize));
        return Ok(new
        {
            success = true, page, pageSize, total, totalPages = (int)Math.Ceiling(total / (double)pageSize),
            data = items.Select(DriverApplicationRowViewModel.From),
        });
    }

    [HttpGet("statistics")]
    public async Task<IActionResult> Statistics() => Ok(new { success = true, data = await verification.StatsAsync() });

    [HttpGet("{driverId:guid}")]
    public async Task<IActionResult> Details(Guid driverId)
    {
        var profile = await verification.DetailsAsync(driverId);
        return profile is null
            ? NotFoundError("Driver application not found.")
            : Ok(new { success = true, data = DriverApplicationViewModel.From(profile, await verification.AssignmentsAsync(driverId), forAdmin: true, Today) });
    }

    [HttpGet("{driverId:guid}/documents/{documentType}")]
    public async Task<IActionResult> Document(Guid driverId, string documentType)
    {
        var stored = await verification.DocumentAsync(driverId, documentType);
        if (stored is null)
        {
            return NotFoundError("Document not found.");
        }
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.ContentDisposition = "inline";
        return File(stored.Content, stored.ContentType);
    }

    [HttpPut("{driverId:guid}/start-review")]
    public async Task<IActionResult> StartReview(Guid driverId) => await Decision(driverId, verification.StartReviewAsync(CurrentUserId, driverId));

    [HttpPut("{driverId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid driverId, DriverReviewRequest? request) =>
        await Decision(driverId, verification.ApproveAsync(CurrentUserId, driverId, request?.Reason));

    [HttpPut("{driverId:guid}/reject")]
    public async Task<IActionResult> Reject(Guid driverId, DriverReviewRequest request) =>
        await Decision(driverId, verification.RejectAsync(CurrentUserId, driverId, request.Reason));

    // Body: { "reason": "…", "fields": ["nid_front", "driving_details", …] }
    [HttpPut("{driverId:guid}/request-resubmission")]
    public async Task<IActionResult> RequestResubmission(Guid driverId, DriverResubmissionRequest request) =>
        await Decision(driverId, verification.RequestResubmissionAsync(CurrentUserId, driverId, request.Reason, request.Fields));

    [HttpPut("{driverId:guid}/assign-bus")]
    public async Task<IActionResult> AssignBus(Guid driverId, AssignBusRequest request)
    {
        var result = await verification.AssignBusAsync(CurrentUserId, driverId, request.BusId!.Value);
        return result.Succeeded
            ? StatusCode(result.StatusCode, new { success = true, message = result.Message, data = DriverAssignmentViewModel.From(result.Value!) })
            : Error(result.StatusCode, result.Message);
    }

    [HttpPut("{driverId:guid}/assignments/{assignmentId:guid}/end")]
    public async Task<IActionResult> EndAssignment(Guid driverId, Guid assignmentId)
    {
        var result = await verification.EndAssignmentAsync(CurrentUserId, driverId, assignmentId);
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, data = DriverAssignmentViewModel.From(result.Value!) })
            : Error(result.StatusCode, result.Message);
    }

    private async Task<IActionResult> Decision(Guid driverId, Task<ServiceResult<DriverProfile>> action)
    {
        var result = await action;
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, data = DriverApplicationViewModel.From(result.Value!, await verification.AssignmentsAsync(driverId), forAdmin: true, Today) })
            : Error(result.StatusCode, result.Message);
    }
}
