using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// Ride share posts. Anyone may browse open rides; creating, deleting and completing need a signed-in user, who
// is always taken from the token (never from a userId in the body). Only the host may delete or complete a post.
[Route("api/rideshare")]
public class RideShareController(RideShareService rides) : ApiControllerBase
{
    // Open rides, newest first: ?destination=Dhanmondi&page=1&pageSize=50 (pageSize at most 100).
    [HttpGet("")]
    public Task<IActionResult> List([FromQuery] string? destination, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (destination?.Trim().Length > RideShareService.MaxPlaceLength)
        {
            return Task.FromResult<IActionResult>(BadRequestError("The destination filter can be at most 200 characters."));
        }
        return Paged(rides.Open(destination), page, pageSize);
    }

    [Authorize]
    [HttpPost("")]
    public async Task<IActionResult> Create(CreateRidePostRequest request)
    {
        var result = await rides.CreateAsync(CurrentUserId,
            new NewRide(request.Source, request.Destination, request.MaxParticipants, request.GenderPreference));
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        var post = RidePostViewModel.From(result.Post!);
        return Created201(new { success = true, message = result.Message, ridePost = post, data = post });
    }

    // A host's posts. The host themselves gets open and completed posts (their "My rides"); everyone else gets only
    // that host's open posts, the same ones the public list shows.
    [HttpGet("user/{userId:guid}")]
    public Task<IActionResult> UserRides(Guid userId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50) =>
        Paged(rides.ByHost(userId, includeCompleted: IsSignedIn && CurrentUserId == userId), page, pageSize);

    [Authorize]
    [HttpDelete("{postId:guid}")]
    public async Task<IActionResult> Delete(Guid postId)
    {
        var result = await rides.DeleteAsync(CurrentUserId, postId);
        return result.Succeeded ? Ok(new { success = true, message = result.Message }) : Error(result.StatusCode, result.Message);
    }

    // Marks the host's ride as completed. Completing it again returns the completed post without changing anything.
    [Authorize]
    [HttpPost("complete")]
    public async Task<IActionResult> Complete(CompleteRideRequest request)
    {
        var result = await rides.CompleteAsync(CurrentUserId, request.PostId!.Value);
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, data = RidePostViewModel.From(result.Post!) })
            : Error(result.StatusCode, result.Message);
    }

    private async Task<IActionResult> Paged(IQueryable<RidePost> query, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new
        {
            success = true,
            count = items.Count,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling(total / (double)pageSize),
            data = items.Select(RidePostViewModel.From),
        });
    }
}

// Requests to join a ride (F12). Every call needs a signed-in user: the rider asking, or the host answering. The user
// always comes from the token; ids in the body are not trusted.
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
