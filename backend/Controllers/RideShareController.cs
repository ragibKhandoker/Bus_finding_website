using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// Ride share (F11). Anyone may browse open rides and read a ride's public details; everything else needs a signed-in
// user, who is always taken from the token (never from a userId in the body). The services decide who may do what:
// only the host manages a ride and its passengers, only the host and approved riders see private details and the
// ride chat, and seat counts are only ever changed under the ride's database lock.
[Route("api/rideshare")]
public class RideShareController(RideShareService rides, RideRequestService requests, RideCommunityService community) : ApiControllerBase
{
    private Guid? ViewerId => IsSignedIn ? CurrentUserId : null;

    // ---- Discovery ----

    // OPEN rides that have not left yet. Filters combine: ?source=&destination=&date=2026-11-10&gender=Female
    // &vehicle=CNG&minSeats=2&minCost=0&maxCost=200&sort=recent|cost|departure|seats&page=1&pageSize=12.
    [HttpGet("")]
    public async Task<IActionResult> List([FromQuery] string? source, [FromQuery] string? destination, [FromQuery] string? date,
        [FromQuery] string? gender, [FromQuery] string? vehicle, [FromQuery] int? minSeats, [FromQuery] decimal? minCost,
        [FromQuery] decimal? maxCost, [FromQuery] string? sort, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (source?.Trim().Length > RideShareService.MaxPlaceLength || destination?.Trim().Length > RideShareService.MaxPlaceLength)
        {
            return BadRequestError("The destination filter can be at most 200 characters.");
        }
        DateOnly? day = null;
        if (!string.IsNullOrWhiteSpace(date))
        {
            if (!DateOnly.TryParseExact(date.Trim(), "yyyy-MM-dd", out var parsed))
            {
                return BadRequestError("Date must look like 2026-11-10.");
            }
            day = parsed;
        }
        string? genderFilter = null;
        if (!string.IsNullOrWhiteSpace(gender) && !gender.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            genderFilter = RideGenderPreference.Normalize(gender);
            if (genderFilter is null)
            {
                return BadRequestError("Gender must be Any, Male or Female.");
            }
        }
        string? vehicleFilter = null;
        if (!string.IsNullOrWhiteSpace(vehicle) && !vehicle.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            vehicleFilter = RideVehicleTypes.Normalize(vehicle);
            if (vehicleFilter is null)
            {
                return BadRequestError("Vehicle must be Car, Bike, CNG or Other.");
            }
        }
        if (minSeats is < 1 or > 19)
        {
            return BadRequestError("Available seats must be from 1 to 19.");
        }
        if (minCost < 0 || maxCost < 0 || (minCost is { } lo && maxCost is { } hi && lo > hi))
        {
            return BadRequestError("Enter a cost range from 0 upwards, with the minimum not above the maximum.");
        }
        var sortBy = string.IsNullOrWhiteSpace(sort) ? RideSorts.Recent : sort.Trim().ToLowerInvariant();
        if (!RideSorts.All.Contains(sortBy))
        {
            return BadRequestError("Sort must be recent, cost, departure or seats.");
        }
        var filter = new RideSearch(source?.Trim(), destination?.Trim(), day, genderFilter, vehicleFilter, minSeats, minCost, maxCost, sortBy, ViewerId);
        return await Paged(rides.Search(filter, DateTime.UtcNow), page, pageSize, knownUnblocked: true);
    }

    // One ride, as the caller may see it: private details (pickup, the host's phone) only for the host and approved
    // riders. Completed, cancelled and expired rides stay readable as history.
    [HttpGet("{postId:guid}")]
    public async Task<IActionResult> Get(Guid postId)
    {
        var post = await rides.FindAsync(postId);
        return post is null ? NotFoundError("Ride post not found.") : Ok(new { success = true, data = await rides.ViewAsync(post, ViewerId) });
    }

    // A host's posts. The host themselves gets every status (their "My rides"); everyone else gets only that host's
    // open posts, the same ones the public list shows.
    [HttpGet("user/{userId:guid}")]
    public Task<IActionResult> UserRides(Guid userId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50) =>
        Paged(rides.ByHost(userId, includeCompleted: IsSignedIn && CurrentUserId == userId), page, pageSize);

    // ---- Hosting ----

    [Authorize]
    [HttpPost("")]
    public async Task<IActionResult> Create(CreateRidePostRequest request)
    {
        var result = await rides.CreateAsync(CurrentUserId, request.ToInput());
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        var post = await rides.ViewAsync(result.Post!, CurrentUserId);
        return Created201(new { success = true, message = result.Message, ridePost = post, data = post });
    }

    [Authorize]
    [HttpPut("{postId:guid}")]
    public async Task<IActionResult> Update(Guid postId, CreateRidePostRequest request) =>
        await RideReply(await rides.UpdateAsync(CurrentUserId, postId, request.ToInput()));

    [Authorize]
    [HttpDelete("{postId:guid}")]
    public async Task<IActionResult> Delete(Guid postId)
    {
        var result = await rides.DeleteAsync(CurrentUserId, postId);
        return result.Succeeded ? Ok(new { success = true, message = result.Message }) : Error(result.StatusCode, result.Message);
    }

    [Authorize]
    [HttpPost("{postId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid postId, CancelRideRequest? request) =>
        await RideReply(await rides.CancelAsync(CurrentUserId, postId, request?.Reason));

    // Marks the host's ride as completed. Completing it again returns the completed post without changing anything.
    [Authorize]
    [HttpPost("{postId:guid}/complete")]
    public async Task<IActionResult> CompleteById(Guid postId) => await RideReply(await rides.CompleteAsync(CurrentUserId, postId));

    // The original form, with { "postId": "…" } in the body.
    [Authorize]
    [HttpPost("complete")]
    public async Task<IActionResult> Complete(CompleteRideRequest request) => await RideReply(await rides.CompleteAsync(CurrentUserId, request.PostId!.Value));

    // ---- My rides ----

    [Authorize]
    [HttpGet("my/hosted")]
    public async Task<IActionResult> Hosted()
    {
        var list = await rides.ViewsAsync(await rides.HostedAsync(CurrentUserId), CurrentUserId);
        return Ok(new { success = true, count = list.Count, data = list });
    }

    // The caller's requests, newest first, each with the ride as the caller sees it.
    [Authorize]
    [HttpGet("my/joined")]
    public async Task<IActionResult> Joined()
    {
        var mine = await requests.ForRequesterAsync(CurrentUserId);
        var latestPerRide = mine.GroupBy(r => r.RidePostId).Select(g => g.First()).ToList();
        var views = (await rides.ViewsAsync(latestPerRide.Select(r => r.RidePost!).ToList(), CurrentUserId)).ToDictionary(v => v.Id);
        return Ok(new
        {
            success = true,
            count = latestPerRide.Count,
            data = latestPerRide.Select(r => new { request = RideRequestViewModel.From(r), ride = views[r.RidePostId.ToString()] }),
        });
    }

    // ---- Join requests ----

    // Body (optional): { "note": "…" }.
    [Authorize]
    [HttpPost("{postId:guid}/requests")]
    public async Task<IActionResult> Join(Guid postId, JoinRideRequest? request)
    {
        var result = await requests.SendAsync(CurrentUserId, postId, request?.Note);
        return result.Succeeded
            ? Created201(new { success = true, message = result.Message, data = RideRequestViewModel.From(result.Request!) })
            : Error(result.StatusCode, result.Message);
    }

    // Every request for the host's ride (pending, approved, rejected, cancelled), each rider with their rating.
    [Authorize]
    [HttpGet("{postId:guid}/requests")]
    public async Task<IActionResult> Requests(Guid postId)
    {
        var (ride, list, problem) = await requests.ForRideAsync(CurrentUserId, postId);
        if (problem is not null)
        {
            return Error(problem.StatusCode, problem.Message);
        }
        var stats = await rides.StatsAsync(list.Select(r => r.RequesterId).Distinct().ToList());
        return Ok(new
        {
            success = true,
            count = list.Count,
            ride = RidePostSummary.From(ride!),
            data = list.Select(r => RideRequestViewModel.From(r, stats.GetValueOrDefault(r.RequesterId, RideUserStats.None), forHost: true)),
        });
    }

    [Authorize]
    [HttpPost("requests/{requestId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid requestId) => RequestReply(await requests.AcceptAsync(CurrentUserId, requestId));

    [Authorize]
    [HttpPost("requests/{requestId:guid}/reject")]
    public async Task<IActionResult> Reject(Guid requestId) => RequestReply(await requests.RejectAsync(CurrentUserId, requestId));

    // The rider withdraws their own pending or approved request.
    [Authorize]
    [HttpDelete("requests/{requestId:guid}")]
    public async Task<IActionResult> CancelRequest(Guid requestId) => RequestReply(await requests.CancelOwnAsync(CurrentUserId, requestId));

    // ---- Passengers ----

    [Authorize]
    [HttpGet("{postId:guid}/passengers")]
    public async Task<IActionResult> Passengers(Guid postId)
    {
        var (ride, list, problem) = await requests.PassengersAsync(CurrentUserId, postId);
        if (problem is not null)
        {
            return Error(problem.StatusCode, problem.Message);
        }
        var stats = await rides.StatsAsync(list.Select(r => r.RequesterId).ToList());
        return Ok(new
        {
            success = true,
            count = list.Count,
            ride = RidePostSummary.From(ride!),
            data = list.Select(r => RideRequestViewModel.From(r, stats.GetValueOrDefault(r.RequesterId, RideUserStats.None), forHost: ride!.UserId == CurrentUserId)),
        });
    }

    [Authorize]
    [HttpDelete("{postId:guid}/passengers/{userId:guid}")]
    public async Task<IActionResult> RemovePassenger(Guid postId, Guid userId) => RequestReply(await requests.RemovePassengerAsync(CurrentUserId, postId, userId));

    // ---- Ride chat ----

    // ?after=2026-11-10T08:30:00Z returns only newer messages (for polling).
    [Authorize]
    [HttpGet("{postId:guid}/messages")]
    public async Task<IActionResult> Messages(Guid postId, [FromQuery] DateTime? after)
    {
        var result = await community.MessagesAsync(CurrentUserId, postId, after?.ToUniversalTime());
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        var (post, messages) = result.Value;
        return Ok(new { success = true, count = messages.Count, data = messages.Select(m => RideMessageViewModel.From(m, post.UserId)) });
    }

    [Authorize]
    [EnableRateLimiting("chat-send")]
    [HttpPost("{postId:guid}/messages")]
    public async Task<IActionResult> SendMessage(Guid postId, RideMessageRequest request)
    {
        var result = await community.SendMessageAsync(CurrentUserId, postId, request.Text);
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        var host = (await rides.FindAsync(postId))?.UserId ?? Guid.Empty;
        return Created201(new { success = true, data = RideMessageViewModel.From(result.Value!, host) });
    }

    // ---- Reviews ----

    // The ride's reviews; for a member of a completed ride also canReview: the people they have not reviewed yet.
    [HttpGet("{postId:guid}/reviews")]
    public async Task<IActionResult> Reviews(Guid postId)
    {
        var result = await community.ReviewsAsync(ViewerId, postId);
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        return Ok(new
        {
            success = true,
            count = result.Value!.Reviews.Count,
            data = result.Value.Reviews.Select(RideReviewViewModel.From),
            canReview = result.Value.CanReview.Select(x => new { userId = x.UserId.ToString(), name = x.Name }),
        });
    }

    [Authorize]
    [HttpPost("{postId:guid}/reviews")]
    public async Task<IActionResult> Review(Guid postId, RideReviewRequest request)
    {
        var result = await community.ReviewAsync(CurrentUserId, postId, request.ReviewedUserId!.Value, request.Rating, request.Comment);
        return result.Succeeded
            ? Created201(new { success = true, message = result.Message, data = RideReviewViewModel.From(result.Value!) })
            : Error(result.StatusCode, result.Message);
    }

    // ---- People, reports and blocks ----

    [HttpGet("users/{userId:guid}/profile")]
    public async Task<IActionResult> Profile(Guid userId)
    {
        var result = await community.ProfileAsync(ViewerId, userId);
        return result.Succeeded ? Ok(new { success = true, data = result.Value }) : Error(result.StatusCode, result.Message);
    }

    [Authorize]
    [EnableRateLimiting("strict")]
    [HttpPost("{postId:guid}/report")]
    public async Task<IActionResult> ReportRide(Guid postId, RideReportRequest request) =>
        ReportReply(await community.ReportAsync(CurrentUserId, postId, null, request.Reason, request.Description));

    [Authorize]
    [EnableRateLimiting("strict")]
    [HttpPost("users/{userId:guid}/report")]
    public async Task<IActionResult> ReportUser(Guid userId, RideReportRequest request) =>
        ReportReply(await community.ReportAsync(CurrentUserId, null, userId, request.Reason, request.Description));

    [Authorize]
    [HttpPost("users/{userId:guid}/block")]
    public async Task<IActionResult> Block(Guid userId)
    {
        var result = await community.BlockAsync(CurrentUserId, userId);
        return result.Succeeded ? Ok(new { success = true, message = result.Message }) : Error(result.StatusCode, result.Message);
    }

    [Authorize]
    [HttpDelete("users/{userId:guid}/block")]
    public async Task<IActionResult> Unblock(Guid userId)
    {
        var result = await community.UnblockAsync(CurrentUserId, userId);
        return result.Succeeded ? Ok(new { success = true, message = result.Message }) : Error(result.StatusCode, result.Message);
    }

    [Authorize]
    [HttpGet("blocks")]
    public async Task<IActionResult> Blocks()
    {
        var list = await community.BlocksAsync(CurrentUserId);
        return Ok(new { success = true, count = list.Count, data = list.Select(b => new { userId = b.BlockedId.ToString(), name = b.Blocked?.Name ?? "", blockedAt = b.CreatedAt }) });
    }

    // ---- Helpers ----

    private async Task<IActionResult> RideReply(RideResult result) => result.Succeeded
        ? Ok(new { success = true, message = result.Message, data = result.Post is null ? null : await rides.ViewAsync(result.Post, CurrentUserId) })
        : Error(result.StatusCode, result.Message);

    private IActionResult RequestReply(RideRequestResult result) => result.Succeeded
        ? Ok(new { success = true, message = result.Message, data = RideRequestViewModel.From(result.Request!), ride = RidePostSummary.From(result.Ride!) })
        : Error(result.StatusCode, result.Message);

    private IActionResult ReportReply(ServiceResult<RideReport> result) => result.Succeeded
        ? Created201(new { success = true, message = result.Message, data = new { _id = result.Value!.Id.ToString(), status = result.Value.Status } })
        : Error(result.StatusCode, result.Message);

    private async Task<IActionResult> Paged(IQueryable<RidePost> query, int page, int pageSize, bool knownUnblocked = false)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        // A first page that is not full already says how many there are, so the count query is skipped.
        var total = page == 1 && items.Count < pageSize ? items.Count : await query.CountAsync();
        return Ok(new
        {
            success = true,
            count = items.Count,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling(total / (double)pageSize),
            data = await rides.ViewsAsync(items, ViewerId, knownUnblocked),
        });
    }
}

// Requests to join a ride (F12), the original routes. Every call needs a signed-in user: the rider asking, or the host
// answering. The user always comes from the token; ids in the body are not trusted. /api/rideshare/... has the same
// actions and more.
[Authorize]
[Route("api/riderequests")]
public class RideRequestController(RideRequestService requests) : ApiControllerBase
{
    // POST /api/riderequests with { "ridePostId": "…" }. /send is the older path for the same thing.
    [HttpPost("")]
    [HttpPost("send")]
    public async Task<IActionResult> Send(SendRideRequestRequest request)
    {
        var result = await requests.SendAsync(CurrentUserId, request.RidePostId!.Value);
        return result.Succeeded
            ? Created201(new { success = true, message = result.Message, data = RideRequestViewModel.From(result.Request!) })
            : Error(result.StatusCode, result.Message);
    }

    // The requests for a ride, for its host only, with how many places are left.
    [HttpGet("ride/{ridePostId:guid}")]
    public async Task<IActionResult> ForRide(Guid ridePostId)
    {
        var (ride, list, problem) = await requests.ForRideAsync(CurrentUserId, ridePostId);
        if (problem is not null)
        {
            return Error(problem.StatusCode, problem.Message);
        }
        return Ok(new { success = true, count = list.Count, ride = RidePostSummary.From(ride!), data = list.Select(RideRequestViewModel.From) });
    }

    // The caller's own requests, each with the ride it is for. Nobody can read someone else's.
    [HttpGet("user/{userId:guid}")]
    public async Task<IActionResult> ForUser(Guid userId)
    {
        if (userId != CurrentUserId)
        {
            return ForbiddenError("You can only see your own ride requests.");
        }
        var list = await requests.ForRequesterAsync(userId);
        return Ok(new { success = true, count = list.Count, data = list.Select(RideRequestViewModel.From) });
    }

    [HttpPut("accept/{requestId:guid}")]
    public async Task<IActionResult> Accept(Guid requestId) => Decided(await requests.AcceptAsync(CurrentUserId, requestId));

    [HttpPut("reject/{requestId:guid}")]
    public async Task<IActionResult> Reject(Guid requestId) => Decided(await requests.RejectAsync(CurrentUserId, requestId));

    private IActionResult Decided(RideRequestResult result) => result.Succeeded
        ? Ok(new { success = true, message = result.Message, data = RideRequestViewModel.From(result.Request!), ride = RidePostSummary.From(result.Ride!) })
        : Error(result.StatusCode, result.Message);
}
