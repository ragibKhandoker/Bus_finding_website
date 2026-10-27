using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

// How the signed-in user is connected to someone they looked up.
public static class FriendRelationships
{
    public const string None = "none";
    public const string Self = "self";
    public const string Friends = "friends";
    public const string RequestSent = "request_sent";
    public const string RequestReceived = "request_received";
}

// The outcome of a friend action: an HTTP status code and the message to show. Request is set on success.
public record FriendResult(int StatusCode, string Message, FriendRequest? Request = null)
{
    public bool Succeeded => StatusCode is >= 200 and < 300;
}

// Friend requests and friendships. The caller passes the signed-in user's id, taken from the token.
public class FriendService(AppDbContext db, NotificationService notifications)
{
    public Task<User?> FindByCodeAsync(string normalizedCode) =>
        db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.FriendCode == normalizedCode);

    public Task<bool> AreFriendsAsync(Guid a, Guid b)
    {
        var (user1, user2) = Friendship.Order(a, b);
        return db.Friendships.AnyAsync(f => f.User1Id == user1 && f.User2Id == user2);
    }

    private Task<FriendRequest?> PendingBetweenAsync(Guid a, Guid b) =>
        db.FriendRequests.AsNoTracking().FirstOrDefaultAsync(r =>
            r.Status == FriendRequestStatus.Pending &&
            ((r.FromUserId == a && r.ToUserId == b) || (r.FromUserId == b && r.ToUserId == a)));

    // The relationship, plus the pending request's id when there is one.
    public async Task<(string Relationship, Guid? RequestId)> RelationshipAsync(Guid me, Guid other)
    {
        if (me == other)
        {
            return (FriendRelationships.Self, null);
        }
        if (await AreFriendsAsync(me, other))
        {
            return (FriendRelationships.Friends, null);
        }
        var pending = await PendingBetweenAsync(me, other);
        return pending is null
            ? (FriendRelationships.None, null)
            : (pending.FromUserId == me ? FriendRelationships.RequestSent : FriendRelationships.RequestReceived, pending.Id);
    }

    public async Task<FriendResult> SendRequestAsync(Guid senderId, Guid receiverId)
    {
        if (senderId == receiverId)
        {
            return new(StatusCodes.Status400BadRequest, "You cannot send a friend request to yourself.");
        }
        var users = await db.Users.AsNoTracking().Where(u => u.Id == senderId || u.Id == receiverId).ToDictionaryAsync(u => u.Id);
        if (!users.TryGetValue(senderId, out var sender) || !users.TryGetValue(receiverId, out var receiver))
        {
            return new(StatusCodes.Status404NotFound, "User not found.");
        }
        if (await AreFriendsAsync(senderId, receiverId))
        {
            return new(StatusCodes.Status409Conflict, "You are already friends.");
        }
        var pending = await PendingBetweenAsync(senderId, receiverId);
        if (pending is not null)
        {
            return PendingConflict(pending.FromUserId == senderId);
        }

        var now = DateTime.UtcNow;
        var request = new FriendRequest { FromUserId = senderId, ToUserId = receiverId, CreatedAt = now, UpdatedAt = now };
        db.FriendRequests.Add(request);
        notifications.Notify(receiverId, "New friend request", $"{sender.Name} wants to add you as a friend.", actionUrl: "/friends");
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
                                           && pg.ConstraintName == "ix_friend_requests_one_pending_per_pair")
        {
            // The two users sent each other a request at the same moment; the database kept only one.
            db.ChangeTracker.Clear();
            var winner = await PendingBetweenAsync(senderId, receiverId);
            return PendingConflict(winner?.FromUserId == senderId);
        }

        request.FromUser = sender;
        request.ToUser = receiver;
        return new(StatusCodes.Status201Created, "Friend request sent.", request);
    }

    private static FriendResult PendingConflict(bool sentByMe) => new(StatusCodes.Status409Conflict, sentByMe
        ? "Friend request already sent."
        : "This person already sent you a request. Accept it from your incoming requests.");

    // Marks the request accepted and records the friendship in one transaction. The status only changes if the
    // request is still pending, so two simultaneous accepts cannot both succeed, and inserting the friendship
    // ignores a pair that is already stored, so it can never be duplicated.
    public Task<FriendResult> AcceptAsync(Guid me, Guid requestId)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var (request, problem) = await LoadForRecipientAsync(me, requestId, "accept");
            if (problem is not null)
            {
                return problem;
            }

            var now = DateTime.UtcNow;
            if (!await MarkHandledAsync(requestId, FriendRequestStatus.Accepted, now))
            {
                return AlreadyHandled;
            }
            var (user1, user2) = Friendship.Order(request!.FromUserId, request.ToUserId);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO friendships (user1id, user2id, created_at) VALUES ({user1}, {user2}, {now}) ON CONFLICT DO NOTHING");
            notifications.Notify(request.FromUserId, "Friend request accepted",
                $"{request.ToUser!.Name} accepted your friend request.", NotificationTypes.Success, "/friends");
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            request.Status = FriendRequestStatus.Accepted;
            request.UpdatedAt = now;
            return new FriendResult(StatusCodes.Status200OK, "Friend request accepted.", request);
        });
    }

    public async Task<FriendResult> RejectAsync(Guid me, Guid requestId)
    {
        var (request, problem) = await LoadForRecipientAsync(me, requestId, "reject");
        if (problem is not null)
        {
            return problem;
        }
        var now = DateTime.UtcNow;
        if (!await MarkHandledAsync(requestId, FriendRequestStatus.Rejected, now))
        {
            return AlreadyHandled;
        }
        request!.Status = FriendRequestStatus.Rejected;
        request.UpdatedAt = now;
        return new(StatusCodes.Status200OK, "Friend request declined.", request);
    }

    private static readonly FriendResult AlreadyHandled = new(StatusCodes.Status409Conflict, "This request was already handled.");

    private async Task<(FriendRequest? Request, FriendResult? Problem)> LoadForRecipientAsync(Guid me, Guid requestId, string action)
    {
        var request = await db.FriendRequests
            .Include(r => r.FromUser)
            .Include(r => r.ToUser)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == requestId);
        if (request is null)
        {
            return (null, new(StatusCodes.Status404NotFound, "Friend request not found."));
        }
        if (request.ToUserId != me)
        {
            return (null, new(StatusCodes.Status403Forbidden, $"Only the person who received this request can {action} it."));
        }
        if (request.Status != FriendRequestStatus.Pending)
        {
            return (null, AlreadyHandled);
        }
        return (request, null);
    }

    // Moves a request out of pending; false if it was no longer pending (handled by another call meanwhile).
    private async Task<bool> MarkHandledAsync(Guid requestId, string status, DateTime now) =>
        await db.FriendRequests
            .Where(r => r.Id == requestId && r.Status == FriendRequestStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, status).SetProperty(r => r.UpdatedAt, now)) == 1;

    public IQueryable<FriendRequest> Incoming(Guid me) => db.FriendRequests
        .Include(r => r.FromUser)
        .Include(r => r.ToUser)
        .Where(r => r.ToUserId == me && r.Status == FriendRequestStatus.Pending)
        .OrderByDescending(r => r.CreatedAt)
        .AsNoTracking();

    public IQueryable<FriendRequest> Sent(Guid me) => db.FriendRequests
        .Include(r => r.FromUser)
        .Include(r => r.ToUser)
        .Where(r => r.FromUserId == me && r.Status == FriendRequestStatus.Pending)
        .OrderByDescending(r => r.CreatedAt)
        .AsNoTracking();

    // The user's friendships ordered by the friend's name; Friend picks the other person out of a row.
    public IQueryable<Friendship> Friendships(Guid me) => db.Friendships
        .Include(f => f.User1)
        .Include(f => f.User2)
        .Where(f => f.User1Id == me || f.User2Id == me)
        .OrderBy(f => f.User1Id == me ? f.User2!.Name : f.User1!.Name)
        .ThenBy(f => f.CreatedAt)
        .AsNoTracking();

    public static User Friend(Friendship friendship, Guid me) => friendship.User1Id == me ? friendship.User2! : friendship.User1!;

    public async Task<List<User>> FriendsOfAsync(Guid me) =>
        (await Friendships(me).ToListAsync()).Select(f => Friend(f, me)).ToList();
}
