using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartCommute.Server.Data;
using SmartCommute.Server.Extensions;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

public record RideResult(int StatusCode, string Message, RidePost? Post = null)
{
    public bool Succeeded => StatusCode is >= 200 and < 300;
}

public record NewRide(string? Source, string? Destination, int MaxParticipants, string? GenderPreference);

// Ride share posts. The host is always the signed-in user passed in from the token; only the host may delete or
// complete a post. Deleted posts are kept (DeletedAt is set) but never shown or changed again.
public class RideShareService(AppDbContext db, WalletService wallets, NotificationService notifications, IOptions<RideShareOptions> options)
{
    public const int MaxPlaceLength = 200;
    public int MaxParticipants => Math.Clamp(options.Value.MaxParticipants, 2, 20);

    private IQueryable<RidePost> Visible => db.RidePosts.Include(p => p.User).Include(p => p.Requests).Where(p => p.DeletedAt == null);

    // Open rides, newest first. destination matches anywhere in the post's destination, ignoring case and the
    // spaces around what was typed.
    public IQueryable<RidePost> Open(string? destination)
    {
        var query = Visible.Where(p => p.Status == RideStatus.Active);
        if (!string.IsNullOrWhiteSpace(destination))
        {
            query = query.Where(p => EF.Functions.ILike(p.Destination, destination.ContainsPattern(), @"\"));
        }
        return query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id).AsNoTracking();
    }

    // A host's posts. The host sees all of theirs (open and completed); anyone else sees only the open ones.
    public IQueryable<RidePost> ByHost(Guid hostId, bool includeCompleted) => Visible
        .Where(p => p.UserId == hostId && (includeCompleted || p.Status == RideStatus.Active))
        .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
        .AsNoTracking();

    public async Task<RideResult> CreateAsync(Guid hostId, NewRide ride)
    {
        var source = ride.Source?.Trim() ?? "";
        var destination = ride.Destination?.Trim() ?? "";
        if (source.Length == 0)
        {
            return new(StatusCodes.Status400BadRequest, "Please enter where the ride starts.");
        }
        if (destination.Length == 0)
        {
            return new(StatusCodes.Status400BadRequest, "Please enter where the ride goes.");
        }
        if (source.Length > MaxPlaceLength || destination.Length > MaxPlaceLength)
        {
            return new(StatusCodes.Status400BadRequest, "Places can be at most 200 characters.");
        }
        if (ride.MaxParticipants < 2 || ride.MaxParticipants > MaxParticipants)
        {
            return new(StatusCodes.Status400BadRequest, $"Maximum participants must be a whole number from 2 to {MaxParticipants}, counting you.");
        }
        if (RideGenderPreference.Normalize(ride.GenderPreference) is not { } preference)
        {
            return new(StatusCodes.Status400BadRequest, "Gender preference must be Any, Male or Female.");
        }
        var host = await db.Users.FindAsync(hostId);
        if (host is null)
        {
            return new(StatusCodes.Status401Unauthorized, "Please sign in to continue.");
        }

        var now = DateTime.UtcNow;
        var post = new RidePost
        {
            UserId = host.Id,
            UserName = host.Name,
            Gender = host.Gender,
            GenderPreference = preference,
            Source = source,
            Destination = destination,
            MaxParticipants = ride.MaxParticipants,
            Status = RideStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.RidePosts.Add(post);
        await db.SaveChangesAsync();
        return new(StatusCodes.Status201Created, "Ride post created.", post);
    }

    // Loads a post for its host to change. Someone else's post is refused (403); a missing or deleted one is 404.
    private async Task<(RidePost? Post, RideResult? Problem)> OwnedAsync(Guid hostId, Guid postId, string action)
    {
        var post = await Visible.FirstOrDefaultAsync(p => p.Id == postId);
        if (post is null)
        {
            return (null, new(StatusCodes.Status404NotFound, "Ride post not found."));
        }
        if (post.UserId != hostId)
        {
            return (null, new(StatusCodes.Status403Forbidden, $"Only the host can {action} this ride."));
        }
        return (post, null);
    }

    // Soft-deletes the post (open or completed) and tells anyone who asked to join an open ride that it is off.
    public async Task<RideResult> DeleteAsync(Guid hostId, Guid postId)
    {
        var (post, problem) = await OwnedAsync(hostId, postId, "delete");
        if (problem is not null)
        {
            return problem;
        }
        var now = DateTime.UtcNow;
        var deleted = await db.RidePosts
            .Where(p => p.Id == postId && p.DeletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.DeletedAt, now).SetProperty(p => p.UpdatedAt, now));
        if (deleted == 0)
        {
            return new(StatusCodes.Status404NotFound, "Ride post not found.");
        }
        if (post!.Status == RideStatus.Active)
        {
            foreach (var request in post.Requests.Where(r => r.Status != RideRequestStatus.Rejected))
            {
                notifications.Notify(request.RequesterId, "Ride cancelled",
                    $"The ride from {post.Source} to {post.Destination} was cancelled by the host.", NotificationTypes.Warning,
                    category: NotificationCategories.Ride, senderId: hostId, relatedId: post.Id);
            }
            await db.SaveChangesAsync();
        }
        return new(StatusCodes.Status200OK, "Ride post deleted.");
    }

    // Open → Completed, once. The status only changes if the post is still open, so a repeated or simultaneous call
    // cannot pay the gem reward twice; it just returns the completed post.
    public async Task<RideResult> CompleteAsync(Guid hostId, Guid postId)
    {
        var (post, problem) = await OwnedAsync(hostId, postId, "complete");
        if (problem is not null)
        {
            return problem;
        }
        if (post!.Status == RideStatus.Completed)
        {
            return new(StatusCodes.Status200OK, "Ride already completed.", post);
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var now = DateTime.UtcNow;
            var changed = await db.RidePosts
                .Where(p => p.Id == postId && p.Status == RideStatus.Active && p.DeletedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, RideStatus.Completed)
                    .SetProperty(p => p.CompletedAt, now)
                    .SetProperty(p => p.UpdatedAt, now));
            if (changed == 1)
            {
                // Everyone on the ride earns the same gems as for a bus trip. The riders are read after the update,
                // which holds the ride's row, so a request accepted a moment ago is counted.
                var riders = await db.RideRequests
                    .Where(r => r.RidePostId == postId && r.Status == RideRequestStatus.Accepted)
                    .Select(r => r.RequesterId)
                    .ToListAsync();
                foreach (var userId in riders.Append(post.UserId))
                {
                    var wallet = await wallets.GetOrCreateAsync(userId);
                    wallets.AddGems(wallet, wallets.Rewards.GemsPerTrip, $"Ride share {post.Source} to {post.Destination}");
                }
                await db.SaveChangesAsync();
            }
            await transaction.CommitAsync();

            var current = await Visible.AsNoTracking().FirstOrDefaultAsync(p => p.Id == postId);
            return current is null
                ? new RideResult(StatusCodes.Status404NotFound, "Ride post not found.")
                : new RideResult(StatusCodes.Status200OK, changed == 1 ? "Ride completed." : "Ride already completed.", current);
        });
    }
}
