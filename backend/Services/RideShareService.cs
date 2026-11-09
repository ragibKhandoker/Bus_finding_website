using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartCommute.Server.Data;
using SmartCommute.Server.Extensions;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

public record RideResult(int StatusCode, string Message, RidePost? Post = null)
{
    public bool Succeeded => StatusCode is >= 200 and < 300;
}

// Ride discovery filters. Every one is optional and they combine. Date is a Dhaka calendar day.
public record RideSearch(
    string? Source = null, string? Destination = null, DateOnly? Date = null, string? GenderPreference = null,
    string? VehicleType = null, int? MinSeats = null, decimal? MinCost = null, decimal? MaxCost = null,
    string? Sort = null, Guid? ViewerId = null);

public static class RideSorts
{
    public const string Recent = "recent";
    public const string Cost = "cost";
    public const string Departure = "departure";
    public const string Seats = "seats";
    public static readonly string[] All = [Recent, Cost, Departure, Seats];
}

// Ride share posts. The host is always the signed-in user passed in from the token; only the host may edit, cancel,
// delete or complete a post (an admin may also cancel one after a report). Deleted posts are kept (DeletedAt is set)
// but never shown or changed again; cancelled, completed and expired rides stay as history.
public class RideShareService(AppDbContext db, WalletService wallets, NotificationService notifications, IOptions<RideShareOptions> options,
    StopService? stops = null)
{
    public const int MaxPlaceLength = RideRules.MaxPlaceLength;
    public int MaxParticipants => Math.Clamp(options.Value.MaxParticipants, 2, 20);

    private IQueryable<RidePost> Visible => db.RidePosts.Include(p => p.User).Include(p => p.Requests).Where(p => p.DeletedAt == null);

    public static string RideUrl(Guid postId) => $"/ride-share/{postId}";

    // ---- Discovery ----

    // OPEN rides that have not departed yet, filtered and sorted. Full, completed, cancelled and expired rides never
    // appear, whatever the client asks; rides of people the viewer blocked (or who blocked the viewer) are left out.
    public IQueryable<RidePost> Search(RideSearch f, DateTime nowUtc)
    {
        var query = Visible.Where(p => p.Status == RideStatus.Active && (p.DepartureAt == null || p.DepartureAt > nowUtc));
        if (!string.IsNullOrWhiteSpace(f.Source))
        {
            query = query.Where(p => EF.Functions.ILike(p.Source, f.Source.ContainsPattern(), @"\"));
        }
        if (!string.IsNullOrWhiteSpace(f.Destination))
        {
            query = query.Where(p => EF.Functions.ILike(p.Destination, f.Destination.ContainsPattern(), @"\"));
        }
        if (f.Date is { } day)
        {
            var from = DhakaTime.ToUtc(day.ToDateTime(TimeOnly.MinValue));
            var to = from.AddDays(1);
            query = query.Where(p => p.DepartureAt >= from && p.DepartureAt < to);
        }
        if (f.GenderPreference is { } gender)
        {
            query = query.Where(p => p.GenderPreference == gender);
        }
        if (f.VehicleType is { } vehicle)
        {
            query = query.Where(p => p.VehicleType == vehicle);
        }
        if (f.MinSeats is { } seats)
        {
            query = query.Where(p => p.MaxParticipants - 1 - p.Requests.Count(r => r.Status == RideRequestStatus.Accepted) >= seats);
        }
        if (f.MinCost is { } min)
        {
            query = query.Where(p => p.CostPerPerson >= min);
        }
        if (f.MaxCost is { } max)
        {
            query = query.Where(p => p.CostPerPerson <= max);
        }
        if (f.ViewerId is { } viewer)
        {
            query = query.Where(p => !db.UserBlocks.Any(b => (b.BlockerId == viewer && b.BlockedId == p.UserId) || (b.BlockerId == p.UserId && b.BlockedId == viewer)));
        }

        query = f.Sort switch
        {
            RideSorts.Cost => query.OrderBy(p => p.CostPerPerson == null).ThenBy(p => p.CostPerPerson).ThenBy(p => p.DepartureAt).ThenBy(p => p.Id),
            RideSorts.Departure => query.OrderBy(p => p.DepartureAt == null).ThenBy(p => p.DepartureAt).ThenByDescending(p => p.CreatedAt).ThenBy(p => p.Id),
            RideSorts.Seats => query.OrderByDescending(p => p.MaxParticipants - 1 - p.Requests.Count(r => r.Status == RideRequestStatus.Accepted))
                .ThenBy(p => p.DepartureAt).ThenBy(p => p.Id),
            // The id breaks ties, so paging never skips or repeats a ride.
            _ => query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id),
        };
        // One query with joins: a page holds a handful of rides, so loading their requests alongside is cheaper than
        // a second round trip to the database.
        return query.AsSingleQuery().AsNoTracking();
    }

    // Open rides, newest first, by destination only (the original list; kept for older clients and tests).
    public IQueryable<RidePost> Open(string? destination)
    {
        var query = Visible.Where(p => p.Status == RideStatus.Active);
        if (!string.IsNullOrWhiteSpace(destination))
        {
            query = query.Where(p => EF.Functions.ILike(p.Destination, destination.ContainsPattern(), @"\"));
        }
        return query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id).AsNoTracking();
    }

    // A host's posts. The host sees all of theirs (every status); anyone else sees only the open ones.
    public IQueryable<RidePost> ByHost(Guid hostId, bool includeCompleted) => Visible
        .Where(p => p.UserId == hostId && (includeCompleted || p.Status == RideStatus.Active))
        .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
        .AsNoTracking();

    // The host's own rides for My Rides: every status, upcoming first, then the rest newest first.
    public Task<List<RidePost>> HostedAsync(Guid hostId) => db.RidePosts
        .Include(p => p.User).Include(p => p.Requests).ThenInclude(r => r.Requester)
        .Where(p => p.UserId == hostId && p.DeletedAt == null)
        .OrderBy(p => p.Status != RideStatus.Active && p.Status != RideStatus.Full)
        .ThenBy(p => p.Status == RideStatus.Active || p.Status == RideStatus.Full ? p.DepartureAt : null)
        .ThenByDescending(p => p.CreatedAt)
        .AsSingleQuery().AsNoTracking()
        .ToListAsync();

    public async Task<RidePost?> FindAsync(Guid postId) => await db.RidePosts
        .Include(p => p.User).Include(p => p.Requests).ThenInclude(r => r.Requester)
        .AsSingleQuery().AsNoTracking()
        .FirstOrDefaultAsync(p => p.Id == postId && p.DeletedAt == null);

    // ---- What the viewer may see ----

    // View models for a list of rides, as the viewer sees them: their own request on each, the hosts' ratings, and
    // private details only where the viewer is the host or an approved rider.
    // knownUnblocked: the posts come from Search, which already left out anyone blocked, so that check is skipped.
    public async Task<List<RidePostViewModel>> ViewsAsync(IReadOnlyList<RidePost> posts, Guid? viewerId, bool knownUnblocked = false)
    {
        if (posts.Count == 0)
        {
            return [];
        }
        var now = DateTime.UtcNow;
        var hostIds = posts.Select(p => p.UserId).Distinct().ToList();
        var stats = await StatsAsync(hostIds);
        Dictionary<Guid, RideRequest> mine = [];
        HashSet<Guid> blocked = [];
        Dictionary<Guid, (string? Phone, string? WhatsApp)> contacts = [];
        if (viewerId is { } viewer)
        {
            var ids = posts.Select(p => p.Id).ToList();
            mine = (await db.RideRequests.AsNoTracking()
                    .Where(r => ids.Contains(r.RidePostId) && r.RequesterId == viewer)
                    .OrderByDescending(r => r.CreatedAt)
                    .ToListAsync())
                .GroupBy(r => r.RidePostId).ToDictionary(g => g.Key, g => g.First());
            blocked = knownUnblocked ? [] : (await db.UserBlocks.AsNoTracking()
                    .Where(b => (b.BlockerId == viewer && hostIds.Contains(b.BlockedId)) || (b.BlockedId == viewer && hostIds.Contains(b.BlockerId)))
                    .Select(b => b.BlockerId == viewer ? b.BlockedId : b.BlockerId)
                    .ToListAsync()).ToHashSet();
            // Approved riders may reach the host by phone or WhatsApp, as the host allowed (see RidePostViewModel).
            var reachable = posts.Where(p => mine.TryGetValue(p.Id, out var r) && r.Status == RideRequestStatus.Accepted
                                             && p.ContactPreference != RideContactPreferences.Chat).Select(p => p.UserId).Distinct().ToList();
            if (reachable.Count > 0)
            {
                contacts = (await db.Users.AsNoTracking().Where(u => reachable.Contains(u.Id)).Select(u => new { u.Id, u.Phone, u.WhatsApp }).ToListAsync())
                    .ToDictionary(u => u.Id, u => (u.Phone, u.WhatsApp));
            }
        }
        return posts.Select(p => RidePostViewModel.From(p, new RideViewer(viewerId, mine.GetValueOrDefault(p.Id),
            stats.GetValueOrDefault(p.UserId, RideUserStats.None), contacts.GetValueOrDefault(p.UserId).Phone, blocked.Contains(p.UserId), now,
            contacts.GetValueOrDefault(p.UserId).WhatsApp))).ToList();
    }

    public async Task<RidePostViewModel> ViewAsync(RidePost post, Guid? viewerId) => (await ViewsAsync([post], viewerId))[0];

    // Average rating from ride reviews, review count and completed rides (hosted or approved on), per person.
    // One query (correlated counts per person), because every round trip to the database costs time.
    public async Task<Dictionary<Guid, RideUserStats>> StatsAsync(IReadOnlyCollection<Guid> userIds)
    {
        if (userIds.Count == 0)
        {
            return [];
        }
        var ids = userIds.Distinct().ToList();
        var rows = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                Average = db.RideReviews.Where(r => r.ReviewedUserId == u.Id).Average(r => (double?)r.Rating),
                Reviews = db.RideReviews.Count(r => r.ReviewedUserId == u.Id),
                Hosted = db.RidePosts.Count(p => p.UserId == u.Id && p.Status == RideStatus.Completed && p.DeletedAt == null),
                Joined = db.RideRequests.Count(r => r.RequesterId == u.Id && r.Status == RideRequestStatus.Accepted
                                                    && r.RidePost!.Status == RideStatus.Completed && r.RidePost.DeletedAt == null),
            })
            .ToListAsync();
        var found = rows.ToDictionary(r => r.Id, r => new RideUserStats(r.Average is { } avg ? Math.Round(avg, 1) : null, r.Reviews, r.Hosted + r.Joined));
        return ids.ToDictionary(id => id, id => found.GetValueOrDefault(id, RideUserStats.None));
    }

    // ---- Creating and editing ----

    // A place as sent (with coordinates), or else a Dhaka bus stop of that name; null when neither is known.
    public async Task<RidePlace?> ResolvePlaceAsync(string? name, double? lat, double? lng)
    {
        if (RideRules.FromInput(name, lat, lng) is { } place)
        {
            return place;
        }
        if (stops is null || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        var stop = await stops.FindAsync(name.Trim());
        return stop is null ? null : new RidePlace(name.Trim(), stop.Lat, stop.Lng);
    }

    private async Task<(RideFields? Fields, string? Error)> CheckAsync(RideInput input)
    {
        var source = await ResolvePlaceAsync(input.Source, input.SourceLat, input.SourceLng);
        var destination = await ResolvePlaceAsync(input.Destination, input.DestinationLat, input.DestinationLng);
        return RideRules.Check(input, source, destination, DateTime.UtcNow, MaxParticipants, DhakaCityArea.Default);
    }

    public async Task<RideResult> CreateAsync(Guid hostId, RideInput input)
    {
        var (fields, error) = await CheckAsync(input);
        if (fields is null)
        {
            return new(StatusCodes.Status400BadRequest, error!);
        }
        var host = await db.Users.FindAsync(hostId);
        if (host is null)
        {
            return new(StatusCodes.Status401Unauthorized, "Please sign in to continue.");
        }
        if (fields.GenderPreference != RideGenderPreference.Any && !RideGenderPreference.Allows(fields.GenderPreference, host.Gender)
            && host.Gender is "Male" or "Female")
        {
            return new(StatusCodes.Status400BadRequest, $"A {fields.GenderPreference.ToLowerInvariant()}-only ride must be hosted by a {fields.GenderPreference.ToLowerInvariant()} rider.");
        }

        var now = DateTime.UtcNow;
        var post = new RidePost
        {
            UserId = host.Id,
            UserName = host.Name,
            Gender = host.Gender,
            Status = RideStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
        Apply(post, fields);
        db.RidePosts.Add(post);
        await db.SaveChangesAsync();
        post.User = host;
        return new(StatusCodes.Status201Created, "Ride post created.", post);
    }

    private static void Apply(RidePost post, RideFields f)
    {
        post.Source = f.Source.Name;
        post.SourceLat = f.Source.Lat;
        post.SourceLng = f.Source.Lng;
        post.Destination = f.Destination.Name;
        post.DestinationLat = f.Destination.Lat;
        post.DestinationLng = f.Destination.Lng;
        post.Distance = Math.Round(GeoService.HaversineKm(f.Source.Lat, f.Source.Lng, f.Destination.Lat, f.Destination.Lng), 1);
        post.DepartureAt = f.DepartureAtUtc;
        post.MaxParticipants = f.MaxParticipants;
        post.GenderPreference = f.GenderPreference;
        post.VehicleType = f.VehicleType;
        post.CostPerPerson = f.CostPerPerson;
        post.Instructions = f.Instructions;
        post.ContactPreference = f.ContactPreference;
        post.PickupDetails = f.PickupDetails;
    }

    // Edits a live ride that has not departed. Seats already given to approved riders are kept: the new maximum
    // cannot be below the people already on the ride, and the status moves between OPEN and FULL to match.
    // Approved riders are told when the route or time changes.
    public Task<RideResult> UpdateAsync(Guid hostId, Guid postId, RideInput input)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            var (fields, error) = await CheckAsync(input);
            if (fields is null)
            {
                return new RideResult(StatusCodes.Status400BadRequest, error!);
            }
            await using var transaction = await db.Database.BeginTransactionAsync();
            await LockAsync(postId);
            var post = await db.RidePosts.Include(p => p.Requests).FirstOrDefaultAsync(p => p.Id == postId && p.DeletedAt == null);
            if (post is null)
            {
                return new RideResult(StatusCodes.Status404NotFound, "Ride post not found.");
            }
            if (post.UserId != hostId)
            {
                return new RideResult(StatusCodes.Status403Forbidden, "Only the host can edit this ride.");
            }
            if (!RideStatus.IsLive(post.Status) || post.DepartureAt <= DateTime.UtcNow)
            {
                return new RideResult(StatusCodes.Status409Conflict, "Only upcoming open or full rides can be edited.");
            }
            var accepted = post.Requests.Where(r => r.Status == RideRequestStatus.Accepted).ToList();
            if (fields.MaxParticipants < accepted.Count + 1)
            {
                return new RideResult(StatusCodes.Status409Conflict, $"{accepted.Count} riders are already approved, so maximum participants must be at least {accepted.Count + 1}.");
            }
            if (accepted.Count > 0 && fields.GenderPreference != RideGenderPreference.Any
                && accepted.Any(r => !RideGenderPreference.Allows(fields.GenderPreference, r.RequesterGender)))
            {
                return new RideResult(StatusCodes.Status409Conflict, "Some approved riders do not match that gender preference.");
            }

            var routeChanged = post.Source != fields.Source.Name || post.Destination != fields.Destination.Name || post.DepartureAt != fields.DepartureAtUtc;
            Apply(post, fields);
            post.Status = accepted.Count + 1 >= post.MaxParticipants ? RideStatus.Full : RideStatus.Active;
            post.UpdatedAt = DateTime.UtcNow;
            post.ReminderSentAt = null;
            if (routeChanged)
            {
                foreach (var rider in accepted)
                {
                    notifications.Notify(rider.RequesterId, "Ride details changed",
                        $"The host changed the ride: {post.Source} to {post.Destination}, leaving {DhakaTime.Format(post.DepartureAt!.Value)[..16].Replace('T', ' ')}.",
                        NotificationTypes.Warning, RideUrl(post.Id), NotificationCategories.Ride, hostId, post.Id);
                }
            }
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return new RideResult(StatusCodes.Status200OK, "Ride updated.", await FindAsync(postId));
        });
    }

    // Holds the ride's row until the transaction ends, so seat changes, edits and status changes on one ride run
    // one after the other.
    internal Task LockAsync(Guid postId) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM ride_posts WHERE id = {postId} FOR UPDATE");

    // ---- Lifecycle ----

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

    // Soft-deletes the post (any status) and tells anyone who asked to join a live ride that it is off.
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
        if (RideStatus.IsLive(post!.Status))
        {
            foreach (var request in post.Requests.Where(r => r.Status is RideRequestStatus.Pending or RideRequestStatus.Accepted))
            {
                notifications.Notify(request.RequesterId, "Ride cancelled",
                    $"The ride from {post.Source} to {post.Destination} was cancelled by the host.", NotificationTypes.Warning,
                    category: NotificationCategories.Ride, senderId: hostId, relatedId: post.Id);
            }
            await db.SaveChangesAsync();
        }
        return new(StatusCodes.Status200OK, "Ride post deleted.");
    }

    // OPEN/FULL -> CANCELLED. Pending requests are closed and everyone who asked or was approved is told. The ride
    // stays as history. byAdmin lets an admin cancel a reported ride.
    public Task<RideResult> CancelAsync(Guid actorId, Guid postId, string? reason, bool byAdmin = false)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await LockAsync(postId);
            var post = await db.RidePosts.Include(p => p.Requests).FirstOrDefaultAsync(p => p.Id == postId && p.DeletedAt == null);
            if (post is null)
            {
                return new RideResult(StatusCodes.Status404NotFound, "Ride post not found.");
            }
            if (post.UserId != actorId && !byAdmin)
            {
                return new RideResult(StatusCodes.Status403Forbidden, "Only the host can cancel this ride.");
            }
            if (post.Status == RideStatus.Cancelled)
            {
                return new RideResult(StatusCodes.Status200OK, "Ride already cancelled.", await FindAsync(postId));
            }
            if (!RideStatus.IsLive(post.Status))
            {
                return new RideResult(StatusCodes.Status409Conflict, "Only open or full rides can be cancelled.");
            }
            reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            if (reason?.Length > 300)
            {
                return new RideResult(StatusCodes.Status400BadRequest, "The reason can be at most 300 characters.");
            }
            var now = DateTime.UtcNow;
            post.Status = RideStatus.Cancelled;
            post.CancelledAt = now;
            post.CancelReason = reason ?? (byAdmin ? "Cancelled by an admin after a report." : null);
            post.UpdatedAt = now;
            foreach (var request in post.Requests.Where(r => r.Status is RideRequestStatus.Pending or RideRequestStatus.Accepted))
            {
                if (request.Status == RideRequestStatus.Pending)
                {
                    request.Status = RideRequestStatus.Cancelled;
                    request.UpdatedAt = now;
                }
                notifications.Notify(request.RequesterId, "Ride cancelled",
                    $"The ride from {post.Source} to {post.Destination} was cancelled{(byAdmin ? " by an admin" : " by the host")}.{(post.CancelReason is { } r ? $" Reason: {r}" : "")}",
                    NotificationTypes.Warning, RideUrl(post.Id), NotificationCategories.Ride, actorId, post.Id);
            }
            if (byAdmin && post.UserId != actorId)
            {
                notifications.Notify(post.UserId, "Ride cancelled by an admin", $"Your ride from {post.Source} to {post.Destination} was cancelled after a report.",
                    NotificationTypes.Warning, RideUrl(post.Id), NotificationCategories.Ride, actorId, post.Id);
            }
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return new RideResult(StatusCodes.Status200OK, "Ride cancelled.", await FindAsync(postId));
        });
    }

    // OPEN/FULL -> COMPLETED, once. The status only changes if the ride is still live, so a repeated or simultaneous
    // call cannot pay the gem reward twice; it just returns the completed post. Pending requests are closed and the
    // approved riders are invited to review.
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
        if (!RideStatus.IsLive(post.Status))
        {
            return new(StatusCodes.Status409Conflict, "A cancelled or expired ride cannot be completed.");
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var now = DateTime.UtcNow;
            var changed = await db.RidePosts
                .Where(p => p.Id == postId && (p.Status == RideStatus.Active || p.Status == RideStatus.Full) && p.DeletedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, RideStatus.Completed)
                    .SetProperty(p => p.CompletedAt, now)
                    .SetProperty(p => p.UpdatedAt, now));
            if (changed == 1)
            {
                await db.RideRequests.Where(r => r.RidePostId == postId && r.Status == RideRequestStatus.Pending)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RideRequestStatus.Cancelled).SetProperty(r => r.UpdatedAt, now));
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
                foreach (var rider in riders)
                {
                    notifications.Notify(rider, "Ride completed", $"Your ride from {post.Source} to {post.Destination} is complete. Leave a review for your co-riders.",
                        NotificationTypes.Success, RideUrl(post.Id), NotificationCategories.Ride, hostId, post.Id);
                }
                await db.SaveChangesAsync();
            }
            await transaction.CommitAsync();

            var current = await FindAsync(postId);
            return current is null
                ? new RideResult(StatusCodes.Status404NotFound, "Ride post not found.")
                : new RideResult(StatusCodes.Status200OK, changed == 1 ? "Ride completed." : "Ride already completed.", current);
        });
    }
}
