using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// The existing clients call these without a token, so they identify the user by the id in the body.
// When a token is sent it must match that id (see CanActAs).
[Route("api/rideshare")]
public class RideShareController(AppDbContext db, WalletService wallets, NotificationService notifications) : ApiControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> List()
    {
        var posts = await db.RidePosts
            .Include(p => p.Requests)
            .Where(p => p.Status == RideStatus.Active)
            .OrderByDescending(p => p.CreatedAt)
            .AsNoTracking()
            .ToListAsync();
        return Ok(new { success = true, count = posts.Count, data = posts.Select(RidePostViewModel.From) });
    }

    [HttpPost("")]
    public async Task<IActionResult> Create(CreateRidePostRequest request)
    {
        var user = await db.Users.FindAsync(request.UserId);
        if (user is null)
        {
            return NotFoundError("User not found.");
        }
        if (!CanActAs(user.Id))
        {
            return ForbiddenError();
        }

        var post = new RidePost
        {
            UserId = user.Id,
            UserName = string.IsNullOrWhiteSpace(request.UserName) ? user.Name : request.UserName.Trim(),
            Gender = string.IsNullOrWhiteSpace(request.Gender) ? user.Gender : request.Gender.Trim(),
            Source = request.Source.Trim(),
            Destination = request.Destination.Trim(),
            MaxParticipants = request.MaxParticipants,
            Distance = request.Distance,
            Fare = request.Fare,
        };
        db.RidePosts.Add(post);
        await db.SaveChangesAsync();
        return Created201(new { success = true, message = "Ride post created.", ridePost = RidePostViewModel.From(post) });
    }

    [HttpGet("user/{userId:guid}")]
    public async Task<IActionResult> UserRides(Guid userId)
    {
        var posts = await db.RidePosts
            .Include(p => p.Requests)
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .AsNoTracking()
            .ToListAsync();
        return Ok(new { success = true, count = posts.Count, data = posts.Select(RidePostViewModel.From) });
    }

    [HttpDelete("{postId:guid}")]
    public async Task<IActionResult> Delete(Guid postId)
    {
        var post = await db.RidePosts.Include(p => p.Requests).FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null)
        {
            return NotFoundError("Ride post not found.");
        }
        if (!CanActAs(post.UserId))
        {
            return ForbiddenError("Only the host can delete this ride.");
        }

        foreach (var request in post.Requests.Where(r => r.Status != RideRequestStatus.Rejected))
        {
            notifications.Notify(request.RequesterId, "Ride cancelled",
                $"The ride from {post.Source} to {post.Destination} was cancelled by the host.", NotificationTypes.Warning);
        }
        db.RidePosts.Remove(post);
        await db.SaveChangesAsync();
        return Ok(new { success = true, message = "Ride post deleted." });
    }

    // Marks the ride as finished and rewards the host and accepted riders with gems.
    [HttpPost("complete")]
    public async Task<IActionResult> Complete(CompleteRideRequest request)
    {
        var post = await db.RidePosts.Include(p => p.Requests).FirstOrDefaultAsync(p => p.Id == request.PostId);
        if (post is null)
        {
            return NotFoundError("Ride post not found.");
        }

        var riders = post.Requests.Where(r => r.Status == RideRequestStatus.Accepted).Select(r => r.RequesterId).ToList();
        var caller = IsSignedIn ? CurrentUserId : request.UserId ?? Guid.Empty;
        if (caller != post.UserId && !riders.Contains(caller) && !IsAdmin)
        {
            return ForbiddenError("Only people on this ride can complete it.");
        }
        if (post.Status == RideStatus.Completed)
        {
            return Ok(new { success = true, message = "Ride already completed." });
        }

        post.Status = RideStatus.Completed;
        foreach (var userId in riders.Append(post.UserId))
        {
            var wallet = await wallets.GetOrCreateAsync(userId);
            wallets.AddGems(wallet, wallets.Rewards.GemsPerTrip, $"Ride share {post.Source} to {post.Destination}");
        }
        await db.SaveChangesAsync();
        return Ok(new { success = true, message = $"Ride completed. Everyone earned {wallets.Rewards.GemsPerTrip} gems." });
    }
}

[Route("api/riderequests")]
public class RideRequestController(AppDbContext db, NotificationService notifications) : ApiControllerBase
{
    [HttpPost("send")]
    public async Task<IActionResult> Send(SendRideRequestRequest request)
    {
        var post = await db.RidePosts.Include(p => p.Requests).FirstOrDefaultAsync(p => p.Id == request.RidePostId);
        var requester = await db.Users.FindAsync(request.RequesterId);
        if (post is null || requester is null)
        {
            return NotFoundError(post is null ? "Ride post not found." : "User not found.");
        }
        if (!CanActAs(requester.Id))
        {
            return ForbiddenError();
        }
        if (post.Status != RideStatus.Active)
        {
            return BadRequestError("This ride is no longer open.");
        }
        if (post.UserId == requester.Id)
        {
            return BadRequestError("You cannot join your own ride.");
        }
        if (post.Requests.Any(r => r.RequesterId == requester.Id && r.Status != RideRequestStatus.Rejected))
        {
            return BadRequestError("You have already requested to join this ride.");
        }
        if (IsFull(post))
        {
            return BadRequestError("This ride is full.");
        }

        var rideRequest = new RideRequest
        {
            RidePostId = post.Id,
            RequesterId = requester.Id,
            RequesterName = string.IsNullOrWhiteSpace(request.RequesterName) ? requester.Name : request.RequesterName.Trim(),
            RequesterGender = string.IsNullOrWhiteSpace(request.RequesterGender) ? requester.Gender : request.RequesterGender.Trim(),
        };
        db.RideRequests.Add(rideRequest);
        notifications.Notify(post.UserId, "New ride request",
            $"{rideRequest.RequesterName} wants to join your ride from {post.Source} to {post.Destination}.");
        await db.SaveChangesAsync();
        return Created201(new { success = true, message = "Ride request sent.", data = RideRequestViewModel.From(rideRequest) });
    }

    [HttpGet("ride/{ridePostId:guid}")]
    public async Task<IActionResult> ForRide(Guid ridePostId)
    {
        var requests = await db.RideRequests
            .Where(r => r.RidePostId == ridePostId)
            .OrderByDescending(r => r.CreatedAt)
            .AsNoTracking()
            .ToListAsync();
        return Ok(new { success = true, count = requests.Count, data = requests.Select(RideRequestViewModel.From) });
    }

    [HttpGet("user/{userId:guid}")]
    public async Task<IActionResult> ForUser(Guid userId)
    {
        var requests = await db.RideRequests
            .Include(r => r.RidePost)
            .Where(r => r.RequesterId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .AsNoTracking()
            .ToListAsync();
        return Ok(new { success = true, count = requests.Count, data = requests.Select(RideRequestViewModel.From) });
    }

    [HttpPut("accept/{requestId:guid}")]
    public async Task<IActionResult> Accept(Guid requestId)
    {
        var rideRequest = await db.RideRequests
            .Include(r => r.RidePost).ThenInclude(p => p!.Requests)
            .FirstOrDefaultAsync(r => r.Id == requestId);
        if (rideRequest is null)
        {
            return NotFoundError("Ride request not found.");
        }
        var post = rideRequest.RidePost!;
        if (!CanActAs(post.UserId))
        {
            return ForbiddenError("Only the host can accept requests.");
        }
        if (rideRequest.Status != RideRequestStatus.Pending)
        {
            return BadRequestError("This request was already handled.");
        }
        if (IsFull(post))
        {
            return BadRequestError("This ride is already full.");
        }

        rideRequest.Status = RideRequestStatus.Accepted;
        notifications.Notify(rideRequest.RequesterId, "Ride request accepted",
            $"You're in! {post.UserName} accepted your request for {post.Source} to {post.Destination}.", NotificationTypes.Success);
        await db.SaveChangesAsync();
        return Ok(new { success = true, message = "Ride request accepted.", data = RideRequestViewModel.From(rideRequest) });
    }

    [HttpPut("reject/{requestId:guid}")]
    public async Task<IActionResult> Reject(Guid requestId)
    {
        var rideRequest = await db.RideRequests.Include(r => r.RidePost).FirstOrDefaultAsync(r => r.Id == requestId);
        if (rideRequest is null)
        {
            return NotFoundError("Ride request not found.");
        }
        var post = rideRequest.RidePost!;
        if (!CanActAs(post.UserId))
        {
            return ForbiddenError("Only the host can decline requests.");
        }
        if (rideRequest.Status != RideRequestStatus.Pending)
        {
            return BadRequestError("This request was already handled.");
        }

        rideRequest.Status = RideRequestStatus.Rejected;
        notifications.Notify(rideRequest.RequesterId, "Ride request declined",
            $"Your request for {post.Source} to {post.Destination} was declined.", NotificationTypes.Warning);
        await db.SaveChangesAsync();
        return Ok(new { success = true, message = "Ride request declined.", data = RideRequestViewModel.From(rideRequest) });
    }

    // MaxParticipants counts the host.
    private static bool IsFull(RidePost post) =>
        post.Requests.Count(r => r.Status == RideRequestStatus.Accepted) + 1 >= post.MaxParticipants;
}
