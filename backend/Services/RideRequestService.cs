using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

public record RideRequestResult(int StatusCode, string Message, RideRequest? Request = null, RidePost? Ride = null)
{
    public bool Succeeded => StatusCode is >= 200 and < 300;
}

// Requests to join a ride share. A ride's participants are its approved ("accepted") requests plus the host, and
// MaxParticipants counts the host, so a ride has MaxParticipants - 1 seats for riders. Every change to a ride's
// seats (approve, rider cancels, host removes a rider) locks the ride's row first, so they run one after the other
// and a ride can never take more riders than it has seats. Every method takes the signed-in user's id from the token.
public class RideRequestService(AppDbContext db, NotificationService notifications)
{
    public const int MaxNoteLength = 200;

    public static int AcceptedCount(RidePost post) => post.Requests.Count(r => r.Status == RideRequestStatus.Accepted);
    public static int SeatsLeft(RidePost post) => Math.Max(0, post.MaxParticipants - 1 - AcceptedCount(post));

    private IQueryable<RidePost> LiveRides => db.RidePosts.Where(p => p.DeletedAt == null);

    private Task LockAsync(Guid postId) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM ride_posts WHERE id = {postId} FOR UPDATE");

    private Task<bool> BlockedAsync(Guid a, Guid b) =>
        db.UserBlocks.AnyAsync(x => (x.BlockerId == a && x.BlockedId == b) || (x.BlockerId == b && x.BlockedId == a));

    public Task<RideRequestResult> SendAsync(Guid requesterId, Guid ridePostId) => SendAsync(requesterId, ridePostId, null);

    public async Task<RideRequestResult> SendAsync(Guid requesterId, Guid ridePostId, string? note)
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
        var live = post.Requests.FirstOrDefault(r => r.RequesterId == requesterId && r.Status is RideRequestStatus.Pending or RideRequestStatus.Accepted);
        if (live is not null)
        {
            return Duplicate(live.Status);
        }
        if (post.Status == RideStatus.Full)
        {
            return new(StatusCodes.Status409Conflict, "This ride is full.");
        }
        if (post.Status != RideStatus.Active)
        {
            return new(StatusCodes.Status409Conflict, "This ride is no longer open.");
        }
        if (post.DepartureAt is { } departure && departure <= DateTime.UtcNow)
        {
            return new(StatusCodes.Status409Conflict, "This ride has already left.");
        }
        if (await BlockedAsync(requesterId, post.UserId))
        {
            return new(StatusCodes.Status403Forbidden, "You cannot join this ride.");
        }
        if (!RideGenderPreference.Allows(post.GenderPreference, requester.Gender))
        {
            return new(StatusCodes.Status403Forbidden, $"This ride is for {post.GenderPreference.ToLowerInvariant()} riders only.");
        }
        if (SeatsLeft(post) == 0)
        {
            return new(StatusCodes.Status409Conflict, "This ride is full.");
        }
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note?.Length > MaxNoteLength)
        {
            return new(StatusCodes.Status400BadRequest, "The note can be at most 200 characters.");
        }

        var now = DateTime.UtcNow;
        var request = new RideRequest
        {
            RidePostId = post.Id,
            RequesterId = requesterId,
            RequesterName = requester.Name,
            RequesterGender = requester.Gender,
            Note = note,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.RideRequests.Add(request);
        notifications.Notify(post.UserId, "New ride request",
            $"{requester.Name} wants to join your ride from {post.Source} to {post.Destination}.", actionUrl: RideShareService.RideUrl(post.Id),
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

    // The approved riders of a ride, for its host and its approved riders only.
    public async Task<(RidePost? Ride, List<RideRequest> Passengers, RideRequestResult? Problem)> PassengersAsync(Guid viewerId, Guid ridePostId)
    {
        var post = await LiveRides.Include(p => p.User).Include(p => p.Requests).ThenInclude(r => r.Requester).AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == ridePostId);
        if (post is null)
        {
            return (null, [], new(StatusCodes.Status404NotFound, "Ride post not found."));
        }
        var approved = post.Requests.Where(r => r.Status == RideRequestStatus.Accepted).OrderBy(r => r.UpdatedAt).ToList();
        if (post.UserId != viewerId && approved.All(r => r.RequesterId != viewerId))
        {
            return (null, [], new(StatusCodes.Status403Forbidden, "Only the host and approved riders can see who is on this ride."));
        }
        return (post, approved, null);
    }

    // A rider's own requests, newest first, with the ride each one is for.
    public Task<List<RideRequest>> ForRequesterAsync(Guid requesterId) => db.RideRequests
        .Include(r => r.RidePost).ThenInclude(p => p!.User)
        .Include(r => r.RidePost).ThenInclude(p => p!.Requests)
        .Where(r => r.RequesterId == requesterId && r.RidePost!.DeletedAt == null)
        .OrderByDescending(r => r.CreatedAt)
        .AsSplitQuery()
        // Request -> ride -> its requests leads back to requests: EF allows that cycle only with identity resolution.
        .AsNoTrackingWithIdentityResolution()
        .ToListAsync();

    // Approves a pending request if a seat is left and revalidates everything under the ride's lock: the ride may
    // have filled up, closed or left a moment ago. When the last seat goes the ride becomes FULL. The request only
    // moves from pending, so repeating the call adds no second rider and sends no second notification.
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

            await LockAsync(post.Id);
            var current = await db.RideRequests.AsNoTracking().Where(r => r.Id == requestId).Select(r => r.Status).SingleAsync();
            if (current != RideRequestStatus.Pending)
            {
                return new RideRequestResult(StatusCodes.Status409Conflict, "This request was already handled.");
            }
            var ride = await db.RidePosts.AsNoTracking().Where(p => p.Id == post.Id)
                .Select(p => new { p.Status, p.MaxParticipants, p.DepartureAt }).SingleAsync();
            var accepted = await db.RideRequests.CountAsync(r => r.RidePostId == post.Id && r.Status == RideRequestStatus.Accepted);
            if (accept)
            {
                if (ride.Status == RideStatus.Full || (ride.Status == RideStatus.Active && accepted + 1 >= ride.MaxParticipants))
                {
                    return new RideRequestResult(StatusCodes.Status409Conflict, "No places are left on this ride, so the request cannot be accepted.");
                }
                if (ride.Status != RideStatus.Active)
                {
                    return new RideRequestResult(StatusCodes.Status409Conflict, "This ride is no longer open.");
                }
                if (ride.DepartureAt is { } departure && departure <= DateTime.UtcNow)
                {
                    return new RideRequestResult(StatusCodes.Status409Conflict, "This ride has already left.");
                }
            }

            var now = DateTime.UtcNow;
            var status = accept ? RideRequestStatus.Accepted : RideRequestStatus.Rejected;
            await db.RideRequests
                .Where(r => r.Id == requestId && r.Status == RideRequestStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, status).SetProperty(r => r.UpdatedAt, now));
            var nowFull = accept && accepted + 2 >= ride.MaxParticipants;
            await db.RidePosts.Where(p => p.Id == post.Id).ExecuteUpdateAsync(s => s
                .SetProperty(p => p.UpdatedAt, now)
                .SetProperty(p => p.Status, p => nowFull ? RideStatus.Full : p.Status));
            if (accept)
            {
                notifications.Notify(request.RequesterId, "Ride request accepted",
                    $"Your ride join request has been accepted. You're in for {post.Source} to {post.Destination}.", NotificationTypes.Success, RideShareService.RideUrl(post.Id),
                    NotificationCategories.Ride, hostId, post.Id);
            }
            else
            {
                notifications.Notify(request.RequesterId, "Ride request declined",
                    $"Your ride join request has been rejected for {post.Source} to {post.Destination}.", NotificationTypes.Warning, RideShareService.RideUrl(post.Id),
                    NotificationCategories.Ride, hostId, post.Id);
            }
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            var updated = await db.RideRequests
                .Include(r => r.Requester)
                .Include(r => r.RidePost).ThenInclude(p => p!.Requests)
                .AsNoTrackingWithIdentityResolution()
                .SingleAsync(r => r.Id == requestId);
            return new RideRequestResult(StatusCodes.Status200OK, accept ? "Ride request accepted." : "Ride request declined.", updated, updated.RidePost);
        });
    }

    // The rider withdraws their own pending or approved request while the ride is still going ahead. An approved
    // rider's seat is given back, and a FULL ride opens again if it has not left yet.
    public Task<RideRequestResult> CancelOwnAsync(Guid requesterId, Guid requestId) =>
        Release(requestId, request => request.RequesterId != requesterId
            ? new RideRequestResult(StatusCodes.Status403Forbidden, "You can only cancel your own request.")
            : null, byHost: false, actorId: requesterId);

    // The host removes an approved rider from their ride; the seat is given back.
    public async Task<RideRequestResult> RemovePassengerAsync(Guid hostId, Guid ridePostId, Guid passengerId)
    {
        var requestId = await db.RideRequests.AsNoTracking()
            .Where(r => r.RidePostId == ridePostId && r.RequesterId == passengerId && r.Status == RideRequestStatus.Accepted)
            .Select(r => (Guid?)r.Id).FirstOrDefaultAsync();
        if (requestId is null)
        {
            var exists = await LiveRides.AnyAsync(p => p.Id == ridePostId);
            if (exists && !await LiveRides.AnyAsync(p => p.Id == ridePostId && p.UserId == hostId))
            {
                return new(StatusCodes.Status403Forbidden, "Only the host can remove riders from this ride.");
            }
            return new(StatusCodes.Status404NotFound, exists ? "That person is not an approved rider on this ride." : "Ride post not found.");
        }
        return await Release(requestId.Value, request => request.RidePost!.UserId != hostId
            ? new RideRequestResult(StatusCodes.Status403Forbidden, "Only the host can remove riders from this ride.")
            : request.Status != RideRequestStatus.Accepted
                ? new RideRequestResult(StatusCodes.Status409Conflict, "That person is no longer on this ride.")
                : null, byHost: true, actorId: hostId);
    }

    private Task<RideRequestResult> Release(Guid requestId, Func<RideRequest, RideRequestResult?> authorize, bool byHost, Guid actorId)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var probe = await db.RideRequests.Include(r => r.RidePost).AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == requestId && r.RidePost!.DeletedAt == null);
            if (probe is null)
            {
                return new RideRequestResult(StatusCodes.Status404NotFound, "Ride request not found.");
            }
            if (authorize(probe) is { } refused)
            {
                return refused;
            }
            await LockAsync(probe.RidePostId);
            var request = await db.RideRequests.Include(r => r.RidePost).SingleAsync(r => r.Id == requestId);
            var post = request.RidePost!;
            if (request.Status is not (RideRequestStatus.Pending or RideRequestStatus.Accepted))
            {
                return new RideRequestResult(StatusCodes.Status409Conflict, "This request is already closed.");
            }
            if (!RideStatus.IsLive(post.Status))
            {
                return new RideRequestResult(StatusCodes.Status409Conflict, "This ride is already closed.");
            }
            var wasApproved = request.Status == RideRequestStatus.Accepted;
            var now = DateTime.UtcNow;
            request.Status = RideRequestStatus.Cancelled;
            request.RemovedByHost = byHost;
            request.UpdatedAt = now;
            post.UpdatedAt = now;
            if (wasApproved && post.Status == RideStatus.Full && (post.DepartureAt is null || post.DepartureAt > now))
            {
                post.Status = RideStatus.Active;
            }
            if (byHost)
            {
                notifications.Notify(request.RequesterId, "Removed from ride",
                    $"The host removed you from the ride from {post.Source} to {post.Destination}.", NotificationTypes.Warning,
                    RideShareService.RideUrl(post.Id), NotificationCategories.Ride, actorId, post.Id);
            }
            else if (wasApproved)
            {
                notifications.Notify(post.UserId, "Rider left your ride",
                    $"{request.RequesterName} cancelled their seat on your ride from {post.Source} to {post.Destination}.", NotificationTypes.Info,
                    RideShareService.RideUrl(post.Id), NotificationCategories.Ride, actorId, post.Id);
            }
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            var updated = await db.RideRequests
                .Include(r => r.Requester)
                .Include(r => r.RidePost).ThenInclude(p => p!.Requests)
                .AsNoTrackingWithIdentityResolution()
                .SingleAsync(r => r.Id == requestId);
            return new RideRequestResult(StatusCodes.Status200OK, byHost ? "Rider removed." : "Request cancelled.", updated, updated.RidePost);
        });
    }
}
