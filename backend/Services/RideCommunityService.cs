using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

// The community side of ride share: the private ride chat, reviews after a completed ride, public profiles,
// reports and blocks. A ride's members are its host and its approved riders; only they may chat, and only they may
// review each other once the ride is completed. Every method takes the signed-in user's id from the token.
public class RideCommunityService(AppDbContext db, NotificationService notifications, RideShareService rides)
{
    public const int MessagePage = 200;
    public const int MaxReportsPerDay = 10;

    private async Task<(RidePost? Post, HashSet<Guid> Members)> MembersAsync(Guid postId)
    {
        var post = await db.RidePosts.Include(p => p.Requests).AsNoTracking().FirstOrDefaultAsync(p => p.Id == postId && p.DeletedAt == null);
        if (post is null)
        {
            return (null, []);
        }
        var members = post.Requests.Where(r => r.Status == RideRequestStatus.Accepted).Select(r => r.RequesterId).Append(post.UserId).ToHashSet();
        return (post, members);
    }

    // ---- Ride chat ----

    // Messages oldest first; after= returns only newer ones, so the page can poll for new messages cheaply.
    public async Task<ServiceResult<(RidePost Post, List<RideMessage> Messages)>> MessagesAsync(Guid viewerId, Guid postId, DateTime? after)
    {
        var (post, members) = await MembersAsync(postId);
        if (post is null)
        {
            return ServiceResult<(RidePost, List<RideMessage>)>.NotFound("Ride post not found.");
        }
        if (!members.Contains(viewerId))
        {
            return ServiceResult<(RidePost, List<RideMessage>)>.Forbidden("Only the host and approved riders can read this ride's chat.");
        }
        var query = db.RideMessages.Include(m => m.Sender).AsNoTracking().Where(m => m.RidePostId == postId);
        if (after is { } since)
        {
            query = query.Where(m => m.CreatedAt > since);
        }
        var latest = await query.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Take(MessagePage).ToListAsync();
        latest.Reverse();
        return ServiceResult<(RidePost, List<RideMessage>)>.Ok((post, latest));
    }

    public async Task<ServiceResult<RideMessage>> SendMessageAsync(Guid senderId, Guid postId, string? text)
    {
        var (post, members) = await MembersAsync(postId);
        if (post is null)
        {
            return ServiceResult<RideMessage>.NotFound("Ride post not found.");
        }
        if (!members.Contains(senderId))
        {
            return ServiceResult<RideMessage>.Forbidden("Only the host and approved riders can write in this ride's chat.");
        }
        if (post.Status == RideStatus.Cancelled)
        {
            return ServiceResult<RideMessage>.Conflict("This ride was cancelled, so its chat is closed.");
        }
        text = text?.Trim() ?? "";
        if (text.Length == 0)
        {
            return ServiceResult<RideMessage>.BadRequest("Write a message.");
        }
        if (text.Length > RideMessage.MaxLength)
        {
            return ServiceResult<RideMessage>.BadRequest("A message can be at most 1000 characters.");
        }
        var message = new RideMessage { RidePostId = postId, SenderId = senderId, Text = text, CreatedAt = DateTime.UtcNow };
        db.RideMessages.Add(message);
        await db.SaveChangesAsync();
        message.Sender = await db.Users.AsNoTracking().FirstAsync(u => u.Id == senderId);
        return ServiceResult<RideMessage>.Created(message);
    }

    // ---- Reviews ----

    public record RideReviews(List<RideReview> Reviews, List<(Guid UserId, string Name)> CanReview);

    // A completed ride's reviews, and whom the viewer may still review on it.
    public async Task<ServiceResult<RideReviews>> ReviewsAsync(Guid? viewerId, Guid postId)
    {
        var (post, members) = await MembersAsync(postId);
        if (post is null)
        {
            return ServiceResult<RideReviews>.NotFound("Ride post not found.");
        }
        var reviews = await db.RideReviews.Include(r => r.Reviewer).Include(r => r.ReviewedUser).AsNoTracking()
            .Where(r => r.RidePostId == postId).OrderByDescending(r => r.CreatedAt).ToListAsync();
        List<(Guid, string)> open = [];
        if (viewerId is { } me && post.Status == RideStatus.Completed && members.Contains(me))
        {
            var done = reviews.Where(r => r.ReviewerId == me).Select(r => r.ReviewedUserId).ToHashSet();
            var targets = members.Where(id => id != me && !done.Contains(id)).ToList();
            var names = await db.Users.AsNoTracking().Where(u => targets.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name);
            open = targets.Select(id => (id, names.GetValueOrDefault(id, ""))).ToList();
        }
        return ServiceResult<RideReviews>.Ok(new RideReviews(reviews, open));
    }

    public async Task<ServiceResult<RideReview>> ReviewAsync(Guid reviewerId, Guid postId, Guid reviewedUserId, int rating, string? comment)
    {
        var (post, members) = await MembersAsync(postId);
        if (post is null)
        {
            return ServiceResult<RideReview>.NotFound("Ride post not found.");
        }
        if (post.Status != RideStatus.Completed)
        {
            return ServiceResult<RideReview>.Conflict("Reviews open once the host marks the ride as completed.");
        }
        if (!members.Contains(reviewerId))
        {
            return ServiceResult<RideReview>.Forbidden("Only the host and approved riders of this ride can review.");
        }
        if (reviewedUserId == reviewerId)
        {
            return ServiceResult<RideReview>.BadRequest("You cannot review yourself.");
        }
        if (!members.Contains(reviewedUserId))
        {
            return ServiceResult<RideReview>.BadRequest("You can only review people who were on this ride.");
        }
        if (rating is < 1 or > 5)
        {
            return ServiceResult<RideReview>.BadRequest("Give 1 to 5 stars.");
        }
        comment = comment?.Trim() ?? "";
        if (comment.Length > RideReview.MaxCommentLength)
        {
            return ServiceResult<RideReview>.BadRequest("A review can be at most 1000 characters.");
        }
        if (await db.RideReviews.AnyAsync(r => r.RidePostId == postId && r.ReviewerId == reviewerId && r.ReviewedUserId == reviewedUserId))
        {
            return ServiceResult<RideReview>.Conflict("You have already reviewed this person for this ride.");
        }
        var reviewer = await db.Users.AsNoTracking().FirstAsync(u => u.Id == reviewerId);
        var review = new RideReview { RidePostId = postId, ReviewerId = reviewerId, ReviewedUserId = reviewedUserId, Rating = rating, Comment = comment };
        db.RideReviews.Add(review);
        notifications.Notify(reviewedUserId, "New ride review", $"{reviewer.Name} gave you {rating} star{(rating == 1 ? "" : "s")} for the ride from {post.Source} to {post.Destination}.",
            NotificationTypes.Info, RideShareService.RideUrl(postId), NotificationCategories.Ride, reviewerId, postId);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return ServiceResult<RideReview>.Conflict("You have already reviewed this person for this ride.");
        }
        return ServiceResult<RideReview>.Created(await db.RideReviews.Include(r => r.Reviewer).Include(r => r.ReviewedUser).Include(r => r.RidePost)
            .AsNoTracking().FirstAsync(r => r.Id == review.Id), "Review saved.");
    }

    // ---- Profiles ----

    public async Task<ServiceResult<RideProfileViewModel>> ProfileAsync(Guid? viewerId, Guid userId)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
        {
            return ServiceResult<RideProfileViewModel>.NotFound("User not found.");
        }
        var stats = (await rides.StatsAsync([userId]))[userId];
        var hosted = await db.RidePosts.CountAsync(p => p.UserId == userId && p.DeletedAt == null);
        var reviews = await db.RideReviews.Include(r => r.Reviewer).Include(r => r.ReviewedUser).Include(r => r.RidePost).AsNoTracking()
            .Where(r => r.ReviewedUserId == userId).OrderByDescending(r => r.CreatedAt).Take(20).ToListAsync();
        var blocked = viewerId is { } me && await db.UserBlocks.AnyAsync(b => b.BlockerId == me && b.BlockedId == userId);
        return ServiceResult<RideProfileViewModel>.Ok(new RideProfileViewModel
        {
            Id = user.Id.ToString(),
            Name = user.Name,
            Gender = user.Gender,
            Verified = user.Pass,
            Bio = string.IsNullOrWhiteSpace(user.Bio) ? null : user.Bio,
            IdentityVerified = user.IdentityVerified,
            MemberSince = user.CreatedAt,
            Rating = stats.Rating,
            ReviewCount = stats.ReviewCount,
            CompletedRides = stats.CompletedRides,
            HostedRides = hosted,
            BlockedByMe = blocked,
            IsMe = viewerId == userId,
            Reviews = reviews.Select(RideReviewViewModel.From).ToList(),
        });
    }

    // ---- Reports ----

    // A report about a ride (its host is the reported person) or about a person. Admins are told; one reporter can
    // keep only one open report per ride or person, and at most MaxReportsPerDay a day.
    public async Task<ServiceResult<RideReport>> ReportAsync(Guid reporterId, Guid? postId, Guid? userId, string? reason, string? description)
    {
        if (RideReportReasons.Normalize(reason) is not { } why)
        {
            return ServiceResult<RideReport>.BadRequest("Choose a reason: unsafe, harassment, fake, spam, no_show or other.");
        }
        description = description?.Trim() ?? "";
        if (description.Length > 1000)
        {
            return ServiceResult<RideReport>.BadRequest("The description can be at most 1000 characters.");
        }
        if (why == "other" && description.Length == 0)
        {
            return ServiceResult<RideReport>.BadRequest("Please describe the problem.");
        }
        RidePost? post = null;
        if (postId is { } pid)
        {
            post = await db.RidePosts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid && p.DeletedAt == null);
            if (post is null)
            {
                return ServiceResult<RideReport>.NotFound("Ride post not found.");
            }
            userId = post.UserId;
        }
        if (userId is { } uid && !await db.Users.AnyAsync(u => u.Id == uid))
        {
            return ServiceResult<RideReport>.NotFound("User not found.");
        }
        if (userId == reporterId)
        {
            return ServiceResult<RideReport>.BadRequest(post is null ? "You cannot report yourself." : "You cannot report your own ride.");
        }
        var since = DateTime.UtcNow.AddDays(-1);
        if (await db.RideReports.CountAsync(r => r.ReporterId == reporterId && r.CreatedAt > since) >= MaxReportsPerDay)
        {
            return ServiceResult<RideReport>.Conflict("You have sent many reports today. Please try again tomorrow.");
        }
        if (await db.RideReports.AnyAsync(r => r.ReporterId == reporterId && r.Status == RideReportStatus.Open && r.RidePostId == postId && r.ReportedUserId == userId))
        {
            return ServiceResult<RideReport>.Conflict("You have already reported this. An admin will look at it.");
        }
        var report = new RideReport { RidePostId = postId, ReporterId = reporterId, ReportedUserId = userId, Reason = why, Description = description };
        db.RideReports.Add(report);
        var admins = await db.Users.AsNoTracking().Where(u => u.Role == UserRoles.Admin).Select(u => u.Id).ToListAsync();
        foreach (var admin in admins)
        {
            notifications.Notify(admin, "New ride share report", post is null ? $"A rider was reported ({why})." : $"A ride from {post.Source} to {post.Destination} was reported ({why}).",
                NotificationTypes.Warning, "/admin", NotificationCategories.Ride, reporterId, report.Id);
        }
        await db.SaveChangesAsync();
        return ServiceResult<RideReport>.Created(report, "Thanks. An admin will review your report.");
    }

    public Task<List<RideReport>> ReportsAsync(string? status) => db.RideReports
        .Include(r => r.RidePost).Include(r => r.Reporter).Include(r => r.ReportedUser).AsNoTracking()
        .Where(r => status == null || r.Status == status)
        .OrderBy(r => r.Status != RideReportStatus.Open).ThenByDescending(r => r.CreatedAt)
        .Take(200)
        .ToListAsync();

    public async Task<ServiceResult<RideReport>> ResolveReportAsync(Guid adminId, Guid reportId, string? status, string? note, bool cancelRide)
    {
        if (status is not (RideReportStatus.Reviewed or RideReportStatus.Dismissed))
        {
            return ServiceResult<RideReport>.BadRequest("Status must be reviewed or dismissed.");
        }
        var report = await db.RideReports.FirstOrDefaultAsync(r => r.Id == reportId);
        if (report is null)
        {
            return ServiceResult<RideReport>.NotFound("Report not found.");
        }
        if (cancelRide && report.RidePostId is { } postId)
        {
            var cancelled = await rides.CancelAsync(adminId, postId, null, byAdmin: true);
            if (!cancelled.Succeeded && cancelled.StatusCode != StatusCodes.Status409Conflict)
            {
                return ServiceResult<RideReport>.BadRequest(cancelled.Message);
            }
            db.ChangeTracker.Clear();
            report = await db.RideReports.FirstAsync(r => r.Id == reportId);
        }
        report.Status = status;
        report.AdminNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        report.ReviewedById = adminId;
        report.ReviewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return ServiceResult<RideReport>.Ok(await db.RideReports.Include(r => r.RidePost).Include(r => r.Reporter).Include(r => r.ReportedUser).AsNoTracking()
            .FirstAsync(r => r.Id == reportId), "Report updated.");
    }

    // ---- Blocks ----

    public async Task<ServiceResult<bool>> BlockAsync(Guid blockerId, Guid userId)
    {
        if (blockerId == userId)
        {
            return ServiceResult<bool>.BadRequest("You cannot block yourself.");
        }
        if (!await db.Users.AnyAsync(u => u.Id == userId))
        {
            return ServiceResult<bool>.NotFound("User not found.");
        }
        if (await db.UserBlocks.AnyAsync(b => b.BlockerId == blockerId && b.BlockedId == userId))
        {
            return ServiceResult<bool>.Ok(true, "Already blocked.");
        }
        db.UserBlocks.Add(new UserBlock { BlockerId = blockerId, BlockedId = userId });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
        }
        return ServiceResult<bool>.Ok(true, "User blocked. You will no longer see their rides, and they cannot join yours.");
    }

    public async Task<ServiceResult<bool>> UnblockAsync(Guid blockerId, Guid userId)
    {
        var removed = await db.UserBlocks.Where(b => b.BlockerId == blockerId && b.BlockedId == userId).ExecuteDeleteAsync();
        return removed == 0 ? ServiceResult<bool>.NotFound("That user is not blocked.") : ServiceResult<bool>.Ok(true, "User unblocked.");
    }

    public Task<List<UserBlock>> BlocksAsync(Guid blockerId) => db.UserBlocks.Include(b => b.Blocked).AsNoTracking()
        .Where(b => b.BlockerId == blockerId).OrderByDescending(b => b.CreatedAt).ToListAsync();
}
