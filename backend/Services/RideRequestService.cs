using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

public record RideRequestResult(int StatusCode, string Message, RideRequest? Request = null, RidePost? Ride = null)
{
    public bool Succeeded => StatusCode is >= 200 and < 300;
}

// Requests to join a ride share. A ride's participants are its accepted requests plus the host, and
// MaxParticipants counts the host, so a ride has MaxParticipants - 1 places for riders.
// Every method takes the signed-in user's id from the token.
public class RideRequestService(AppDbContext db, NotificationService notifications)
{
    public static int AcceptedCount(RidePost post) => post.Requests.Count(r => r.Status == RideRequestStatus.Accepted);
    public static int SeatsLeft(RidePost post) => Math.Max(0, post.MaxParticipants - 1 - AcceptedCount(post));

    private IQueryable<RidePost> LiveRides => db.RidePosts.Where(p => p.DeletedAt == null);

    public async Task<RideRequestResult> SendAsync(Guid requesterId, Guid ridePostId)
    {
        var post = await LiveRides.Include(p => p.Requests).AsNoTracking().FirstOrDefaultAsync(p => p.Id == ridePostId);
        var requester = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == requesterId);
        if (post is null)
        {
            return new(StatusCodes.Status404NotFound, "Ride post not found.");
        }
        if (requester is null)
        {
            return new(StatusCodes.Status401Unauthorized, "Please sign in to continue.");
        }
        if (post.UserId == requesterId)
        {
            return new(StatusCodes.Status400BadRequest, "You cannot join your own ride.");
        }
        if (post.Status != RideStatus.Active)
        {
            return new(StatusCodes.Status409Conflict, "This ride is no longer open.");
        }
        var live = post.Requests.FirstOrDefault(r => r.RequesterId == requesterId && r.Status != RideRequestStatus.Rejected);
        if (live is not null)
        {
            return Duplicate(live.Status);
        }
        if (!RideGenderPreference.Allows(post.GenderPreference, requester.Gender))
        {
            return new(StatusCodes.Status403Forbidden, $"This ride is for {post.GenderPreference.ToLowerInvariant()} riders only.");
        }
        if (SeatsLeft(post) == 0)
        {
            return new(StatusCodes.Status409Conflict, "This ride is full.");
        }

        var now = DateTime.UtcNow;
        var request = new RideRequest
        {
            RidePostId = post.Id,
            RequesterId = requesterId,
            RequesterName = requester.Name,
            RequesterGender = requester.Gender,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.RideRequests.Add(request);
        notifications.Notify(post.UserId, "New ride request",
            $"{requester.Name} wants to join your ride from {post.Source} to {post.Destination}.", actionUrl: "/ride-share",
            category: NotificationCategories.Ride, senderId: requesterId, relatedId: post.Id);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
                                           && pg.ConstraintName == "ix_ride_requests_one_live_per_rider")
        {
            // The same rider pressed Join twice at once; the database kept the first request.
            db.ChangeTracker.Clear();
            return Duplicate(RideRequestStatus.Pending);
        }
        request.Requester = requester;
        request.RidePost = post;
        return new(StatusCodes.Status201Created, "Ride request sent.", request, post);
    }

    private static RideRequestResult Duplicate(string status) => new(StatusCodes.Status409Conflict, status == RideRequestStatus.Accepted
        ? "You have already joined this ride."
        : "You have already requested to join this ride.");

    // The requests for a ride, newest first, for its host only.
    public async Task<(RidePost? Ride, List<RideRequest> Requests, RideRequestResult? Problem)> ForRideAsync(Guid hostId, Guid ridePostId)
    {
        var post = await LiveRides.Include(p => p.Requests).AsNoTracking().FirstOrDefaultAsync(p => p.Id == ridePostId);
        if (post is null)
        {
            return (null, [], new(StatusCodes.Status404NotFound, "Ride post not found."));
        }
        if (post.UserId != hostId)
        {
            return (null, [], new(StatusCodes.Status403Forbidden, "Only the host can see requests for this ride."));
        }
        var requests = await db.RideRequests
            .Include(r => r.Requester)
            .Where(r => r.RidePostId == ridePostId)
            .OrderByDescending(r => r.CreatedAt)
            .AsNoTracking()
            .ToListAsync();
        return (post, requests, null);
    }

    // A rider's own requests, newest first, with the ride each one is for.
    public Task<List<RideRequest>> ForRequesterAsync(Guid requesterId) => db.RideRequests
        .Include(r => r.RidePost).ThenInclude(p => p!.User)
        .Include(r => r.RidePost).ThenInclude(p => p!.Requests)
        .Where(r => r.RequesterId == requesterId && r.RidePost!.DeletedAt == null)
        .OrderByDescending(r => r.CreatedAt)
        .AsNoTracking()
        .ToListAsync();

    // Accepts a pending request if a place is left. The ride's row is locked for the rest of the transaction, so
    // two accepts for the same ride run one after the other and the second sees the first one's rider: the ride can
    // never take more riders than it has places. The request only moves from pending, so repeating the call adds no
    // second participant and sends no second notification.
    public Task<RideRequestResult> AcceptAsync(Guid hostId, Guid requestId) => Decide(hostId, requestId, accept: true);

    public Task<RideRequestResult> RejectAsync(Guid hostId, Guid requestId) => Decide(hostId, requestId, accept: false);

    private Task<RideRequestResult> Decide(Guid hostId, Guid requestId, bool accept)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var request = await db.RideRequests.Include(r => r.RidePost).AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == requestId && r.RidePost!.DeletedAt == null);
            if (request is null)
            {
                return new RideRequestResult(StatusCodes.Status404NotFound, "Ride request not found.");
            }
            var post = request.RidePost!;
            if (post.UserId != hostId)
            {
                return new RideRequestResult(StatusCodes.Status403Forbidden, accept ? "Only the host can accept requests." : "Only the host can decline requests.");
            }

            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM ride_posts WHERE id = {post.Id} FOR UPDATE");
            var current = await db.RideRequests.AsNoTracking().Where(r => r.Id == requestId).Select(r => r.Status).SingleAsync();
            if (current != RideRequestStatus.Pending)
            {
                return new RideRequestResult(StatusCodes.Status409Conflict, "This request was already handled.");
            }
            if (accept)
            {
                // Read again under the lock: the host may have completed the ride a moment ago.
                var rideStatus = await db.RidePosts.AsNoTracking().Where(p => p.Id == post.Id).Select(p => p.Status).SingleAsync();
                if (rideStatus != RideStatus.Active)
                {
                    return new RideRequestResult(StatusCodes.Status409Conflict, "This ride is no longer open.");
                }
                var accepted = await db.RideRequests.CountAsync(r => r.RidePostId == post.Id && r.Status == RideRequestStatus.Accepted);
                if (accepted + 1 >= post.MaxParticipants)
                {
                    return new RideRequestResult(StatusCodes.Status409Conflict, "No places are left on this ride, so the request cannot be accepted.");
                }
            }

            var now = DateTime.UtcNow;
            var status = accept ? RideRequestStatus.Accepted : RideRequestStatus.Rejected;
            await db.RideRequests
                .Where(r => r.Id == requestId && r.Status == RideRequestStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, status).SetProperty(r => r.UpdatedAt, now));
            await db.RidePosts.Where(p => p.Id == post.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.UpdatedAt, now));
            if (accept)
            {
                notifications.Notify(request.RequesterId, "Ride request accepted",
                    $"Your ride join request has been accepted. You're in for {post.Source} to {post.Destination}.", NotificationTypes.Success, "/ride-share",
                    NotificationCategories.Ride, hostId, post.Id);
            }
            else
            {
                notifications.Notify(request.RequesterId, "Ride request declined",
                    $"Your ride join request has been rejected for {post.Source} to {post.Destination}.", NotificationTypes.Warning, "/ride-share",
                    NotificationCategories.Ride, hostId, post.Id);
            }
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            var updated = await db.RideRequests
                .Include(r => r.Requester)
                .Include(r => r.RidePost).ThenInclude(p => p!.Requests)
                .AsNoTracking()
                .SingleAsync(r => r.Id == requestId);
            return new RideRequestResult(StatusCodes.Status200OK, accept ? "Ride request accepted." : "Ride request declined.", updated, updated.RidePost);
        });
    }
}
