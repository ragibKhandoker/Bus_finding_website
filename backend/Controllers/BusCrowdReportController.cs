using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// F19: crowd level and seat availability reports. Anyone can read them (without who made them); submitting, editing,
// deleting and the user's own history need a sign-in, and the reporter is always the one in the token.
[Route("api/bus-crowd-reports")]
public class BusCrowdReportController(BusCrowdReportService reports) : ApiControllerBase
{
    private Guid ViewerId => IsSignedIn ? CurrentUserId : Guid.Empty;

    private CrowdReportViewModel View(BusCrowdReport r, DateTime now) => CrowdReportViewModel.From(r, ViewerId, now);

    [Authorize]
    [EnableRateLimiting("crowd-report")]
    [HttpPost]
    public async Task<IActionResult> Create(CrowdReportRequest request)
    {
        var result = await reports.CreateAsync(CurrentUserId, request);
        return result.Succeeded
            ? StatusCode(result.StatusCode, new { success = true, message = result.Message, data = View(result.Value!, DateTime.UtcNow) })
            : Error(result.StatusCode, result.Message);
    }

    [Authorize]
    [HttpGet("my")]
    public async Task<IActionResult> My([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (items, total) = await reports.MineAsync(CurrentUserId, page, pageSize);
        var now = DateTime.UtcNow;
        return Ok(new { success = true, page, pageSize, total, totalPages = (int)Math.Ceiling(total / (double)pageSize), data = items.Select(r => View(r, now)) });
    }

    [AllowAnonymous]
    [HttpGet]
    public Task<IActionResult> List([FromQuery] string? bus, [FromQuery] Guid? busId, [FromQuery] string? stop, [FromQuery] string? crowdLevel,
        [FromQuery] string? seatAvailability, [FromQuery] string? date, [FromQuery] string? freshness, [FromQuery] string? sort,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 12) =>
        Search(bus, busId, stop, crowdLevel, seatAvailability, date, freshness, sort, page, pageSize);

    [AllowAnonymous]
    [HttpGet("bus/{busId:guid}")]
    public Task<IActionResult> ForBus(Guid busId, [FromQuery] string? crowdLevel, [FromQuery] string? seatAvailability, [FromQuery] string? date,
        [FromQuery] string? freshness, [FromQuery] string? sort, [FromQuery] int page = 1, [FromQuery] int pageSize = 12) =>
        Search(null, busId, null, crowdLevel, seatAvailability, date, freshness, sort, page, pageSize);

    private async Task<IActionResult> Search(string? bus, Guid? busId, string? stop, string? crowdLevel, string? seatAvailability, string? date,
        string? freshness, string? sort, int page, int pageSize)
    {
        string? crowd = null, seats = null;
        if (!string.IsNullOrWhiteSpace(crowdLevel) && (crowd = BusCrowdReportService.NormalizeCrowd(crowdLevel)) is null)
        {
            return BadRequestError("crowdLevel must be LOW, MEDIUM or HIGH.");
        }
        if (!string.IsNullOrWhiteSpace(seatAvailability) && (seats = BusCrowdReportService.NormalizeSeats(seatAvailability)) is null)
        {
            return BadRequestError("seatAvailability must be AVAILABLE, LIMITED or NO_SEATS.");
        }
        DateOnly? day = null;
        if (!string.IsNullOrWhiteSpace(date))
        {
            if (!DateOnly.TryParseExact(date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                return BadRequestError("date must look like 2026-11-10.");
            }
            day = d;
        }
        var fresh = string.IsNullOrWhiteSpace(freshness) ? CrowdReportFilter.Current
            : CrowdReportFilter.Freshnesses.FirstOrDefault(x => string.Equals(x, freshness.Trim(), StringComparison.OrdinalIgnoreCase));
        if (fresh is null)
        {
            return BadRequestError("freshness must be current, all, VERY_RECENT, RECENT, AGING or EXPIRED.");
        }
        var order = string.IsNullOrWhiteSpace(sort) ? CrowdReportFilter.SortRecent
            : CrowdReportFilter.Sorts.FirstOrDefault(x => string.Equals(x, sort.Trim(), StringComparison.OrdinalIgnoreCase));
        if (order is null)
        {
            return BadRequestError("sort must be recent, least-crowded, most-crowded or seats.");
        }
        string? Short(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim().Length > 100 ? text.Trim()[..100] : text.Trim();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var now = DateTime.UtcNow;
        var (items, total) = await reports.SearchAsync(new CrowdReportFilter(Short(bus), busId, Short(stop), crowd, seats, day, fresh, order, page, pageSize), now);
        return Ok(new { success = true, page, pageSize, total, totalPages = (int)Math.Ceiling(total / (double)pageSize), data = items.Select(r => View(r, now)) });
    }

    // Each route reported in the last hour, with its latest report. Never includes expired reports.
    [AllowAnonymous]
    [HttpGet("live")]
    public async Task<IActionResult> Live()
    {
        var now = DateTime.UtcNow;
        var live = await reports.LiveAsync(now);
        return Ok(new
        {
            success = true,
            generatedAt = now,
            data = live.Select(x => new CrowdBusSummaryViewModel
            {
                BusId = x.Latest.BusId.ToString(), BusName = x.Latest.BusName, RouteNumber = x.Latest.RouteNumber, RouteName = x.Latest.RouteName,
                ReportCount = x.Count, Latest = View(x.Latest, now),
            }),
        });
    }

    [AllowAnonymous]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var report = await reports.FindAsync(id);
        return report is null ? NotFoundError("Report not found.") : Ok(new { success = true, data = View(report, DateTime.UtcNow) });
    }

    [Authorize]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, CrowdReportRequest request)
    {
        var result = await reports.UpdateAsync(CurrentUserId, id, request);
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, data = View(result.Value!, DateTime.UtcNow) })
            : Error(result.StatusCode, result.Message);
    }

    [Authorize]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await reports.DeleteAsync(CurrentUserId, id);
        return result.Succeeded ? Ok(new { success = true, message = result.Message }) : Error(result.StatusCode, result.Message);
    }
}
