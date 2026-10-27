using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

public record ReactionCounts(int Likes, int Dislikes);

public record ReviewResult<T>(int StatusCode, string Message, T? Value = default)
{
    public bool Succeeded => StatusCode is >= 200 and < 300;
}

public record ReactionResult(int Likes, int Dislikes, string? MyReaction);

// Bus reviews (each with a 1-5 star rating), replies and like/dislike reactions. The author or reacting user is
// always the signed-in user passed in from the token. Only a review's author may edit it; the author or an admin
// may delete it.
public class ReviewService(AppDbContext db)
{
    public const string Like = "like";
    public const string Dislike = "dislike";

    // A bus's rating summary, worked out by the database. Only rated reviews count; replies have no rating.
    public async Task<RatingSummaryViewModel> SummaryAsync(Guid busId)
    {
        var stars = await db.Reviews
            .Where(r => r.BusId == busId && r.Rating != null)
            .GroupBy(r => r.Rating!.Value)
            .Select(g => new { Stars = g.Key, Count = g.Count() })
            .ToListAsync();
        var totalReviews = await db.Reviews.CountAsync(r => r.BusId == busId);
        var total = stars.Sum(s => s.Count);
        return new RatingSummaryViewModel
        {
            BusId = busId.ToString(),
            AverageRating = total == 0 ? null : Math.Round(stars.Sum(s => s.Stars * (double)s.Count) / total, 1),
            TotalRatings = total,
            TotalReviews = totalReviews,
            Distribution = Enumerable.Range(1, 5).ToDictionary(n => n.ToString(), n => stars.FirstOrDefault(s => s.Stars == n)?.Count ?? 0),
        };
    }

    // Average and count for every rated bus, for lists that show many buses at once.
    public Task<List<(Guid BusId, double Average, int Count)>> AllSummariesAsync() => db.Reviews
        .Where(r => r.Rating != null)
        .GroupBy(r => r.BusId)
        .Select(g => new ValueTuple<Guid, double, int>(g.Key, g.Average(r => (double)r.Rating!.Value), g.Count()))
        .ToListAsync();

    // One page of a bus's reviews, newest first, each with its replies, reaction counts and the reader's reaction.
    public async Task<(List<ReviewViewModel> Reviews, int Total)> ForBusAsync(Guid busId, Guid? reader, int page, int pageSize)
    {
        var query = db.Reviews.Where(r => r.BusId == busId);
        var total = await query.CountAsync();
        var reviews = await query
            .Include(r => r.User)
            .Include(r => r.Replies).ThenInclude(x => x.User)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync();
        var ids = reviews.Select(r => r.Id).ToList();
        var counts = await CountsAsync(ids);
        var mine = reader is { } me
            ? await db.ReviewReactions.Where(x => ids.Contains(x.ReviewId) && x.UserId == me).ToDictionaryAsync(x => x.ReviewId, x => x.IsLike ? Like : Dislike)
            : [];
        return (reviews.Select(r => ReviewViewModel.From(r, counts.GetValueOrDefault(r.Id, new ReactionCounts(0, 0)), mine.GetValueOrDefault(r.Id))).ToList(), total);
    }

    private async Task<Dictionary<Guid, ReactionCounts>> CountsAsync(List<Guid> reviewIds) =>
        (await db.ReviewReactions
            .Where(x => reviewIds.Contains(x.ReviewId))
            .GroupBy(x => x.ReviewId)
            .Select(g => new { ReviewId = g.Key, Likes = g.Count(x => x.IsLike), Dislikes = g.Count(x => !x.IsLike) })
            .ToListAsync())
        .ToDictionary(x => x.ReviewId, x => new ReactionCounts(x.Likes, x.Dislikes));

    private async Task<ReviewViewModel> ViewAsync(Guid reviewId, Guid reader)
    {
        var review = await db.Reviews.Include(r => r.User).Include(r => r.Replies).ThenInclude(x => x.User)
            .AsSplitQuery().AsNoTracking().SingleAsync(r => r.Id == reviewId);
        var counts = (await CountsAsync([reviewId])).GetValueOrDefault(reviewId, new ReactionCounts(0, 0));
        var mine = await db.ReviewReactions.Where(x => x.ReviewId == reviewId && x.UserId == reader).Select(x => (bool?)x.IsLike).FirstOrDefaultAsync();
        return ReviewViewModel.From(review, counts, mine is null ? null : mine.Value ? Like : Dislike);
    }

    private static string? CleanText(string? text, out string? problem, string what = "review")
    {
        var trimmed = text?.Trim() ?? "";
        problem = trimmed.Length == 0 ? $"Please write your {what}."
            : trimmed.Length > Review.MaxCommentLength ? $"A {what} can be at most 1000 characters."
            : null;
        return problem is null ? trimmed : null;
    }

    private static bool ValidRating(int? rating) => rating is >= 1 and <= 5;

    public async Task<ReviewResult<ReviewViewModel>> CreateAsync(Guid authorId, Guid busId, int? rating, string? comment)
    {
        if (!ValidRating(rating))
        {
            return new(StatusCodes.Status400BadRequest, "A rating must be from 1 to 5 stars.");
        }
        if (CleanText(comment, out var problem) is not { } text)
        {
            return new(StatusCodes.Status400BadRequest, problem!);
        }
        if (!await db.Buses.AnyAsync(b => b.Id == busId))
        {
            return new(StatusCodes.Status404NotFound, "Bus not found.");
        }
        var author = await db.Users.FindAsync(authorId);
        if (author is null)
        {
            return new(StatusCodes.Status401Unauthorized, "Please sign in to continue.");
        }

        var now = DateTime.UtcNow;
        var review = new Review { BusId = busId, UserId = authorId, UserName = author.Name, Rating = rating, Comment = text, CreatedAt = now, UpdatedAt = now };
        db.Reviews.Add(review);
        await db.SaveChangesAsync();
        return new(StatusCodes.Status201Created, "Review posted.", await ViewAsync(review.Id, authorId));
    }

    public async Task<ReviewResult<ReviewViewModel>> UpdateAsync(Guid authorId, Guid reviewId, int? rating, string? comment)
    {
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId);
        if (review is null)
        {
            return new(StatusCodes.Status404NotFound, "Review not found.");
        }
        if (review.UserId != authorId)
        {
            return new(StatusCodes.Status403Forbidden, "You can only edit your own review.");
        }
        if (rating is null && comment is null)
        {
            return new(StatusCodes.Status400BadRequest, "Change the rating or the text.");
        }
        if (rating is not null)
        {
            if (!ValidRating(rating))
            {
                return new(StatusCodes.Status400BadRequest, "A rating must be from 1 to 5 stars.");
            }
            review.Rating = rating;
        }
        if (comment is not null)
        {
            if (CleanText(comment, out var problem) is not { } text)
            {
                return new(StatusCodes.Status400BadRequest, problem!);
            }
            review.Comment = text;
        }
        review.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return new(StatusCodes.Status200OK, "Review updated.", await ViewAsync(reviewId, authorId));
    }

    // Deleting a review also deletes its replies and reactions (cascading foreign keys).
    public async Task<ReviewResult<Guid>> DeleteAsync(Guid userId, bool isAdmin, Guid reviewId)
    {
        var review = await db.Reviews.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reviewId);
        if (review is null)
        {
            return new(StatusCodes.Status404NotFound, "Review not found.");
        }
        if (review.UserId != userId && !isAdmin)
        {
            return new(StatusCodes.Status403Forbidden, "You can only delete your own review.");
        }
        await db.Reviews.Where(r => r.Id == reviewId).ExecuteDeleteAsync();
        return new(StatusCodes.Status200OK, "Review deleted.", review.BusId);
    }

    public async Task<ReviewResult<ReplyViewModel>> ReplyAsync(Guid authorId, Guid reviewId, Guid? busId, string? comment)
    {
        if (CleanText(comment, out var problem, "reply") is not { } text)
        {
            return new(StatusCodes.Status400BadRequest, problem!);
        }
        var review = await db.Reviews.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reviewId);
        if (review is null)
        {
            return new(StatusCodes.Status404NotFound, "Review not found.");
        }
        if (busId is not null && busId != review.BusId)
        {
            return new(StatusCodes.Status400BadRequest, "That review is about a different bus.");
        }
        var author = await db.Users.FindAsync(authorId);
        if (author is null)
        {
            return new(StatusCodes.Status401Unauthorized, "Please sign in to continue.");
        }

        var reply = new ReviewReply { ReviewId = reviewId, UserId = authorId, UserName = author.Name, Comment = text, CreatedAt = DateTime.UtcNow };
        db.ReviewReplies.Add(reply);
        await db.SaveChangesAsync();
        return new(StatusCodes.Status201Created, "Reply added.", ReplyViewModel.From(reply));
    }

    public async Task<ReviewResult<Guid>> DeleteReplyAsync(Guid userId, bool isAdmin, Guid replyId)
    {
        var reply = await db.ReviewReplies.AsNoTracking().FirstOrDefaultAsync(r => r.Id == replyId);
        if (reply is null)
        {
            return new(StatusCodes.Status404NotFound, "Reply not found.");
        }
        if (reply.UserId != userId && !isAdmin)
        {
            return new(StatusCodes.Status403Forbidden, "You can only delete your own reply.");
        }
        await db.ReviewReplies.Where(r => r.Id == replyId).ExecuteDeleteAsync();
        return new(StatusCodes.Status200OK, "Reply deleted.", reply.ReviewId);
    }

    // Like or dislike a review. Choosing the reaction you already have removes it; choosing the other one switches
    // it. Each step is one SQL statement on the (review, user) primary key, so there is never more than one reaction
    // per user per review, and the counts come from the reactions themselves.
    public async Task<ReviewResult<ReactionResult>> ReactAsync(Guid userId, Guid reviewId, bool isLike)
    {
        if (!await db.Reviews.AnyAsync(r => r.Id == reviewId))
        {
            return new(StatusCodes.Status404NotFound, "Review not found.");
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var removed = await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM review_reactions WHERE review_id = {reviewId} AND user_id = {userId} AND is_like = {isLike}");
            if (removed == 0)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO review_reactions (review_id, user_id, is_like) VALUES ({reviewId}, {userId}, {isLike})
                    ON CONFLICT (review_id, user_id) DO UPDATE SET is_like = EXCLUDED.is_like
                    """);
            }
            var counts = (await CountsAsync([reviewId])).GetValueOrDefault(reviewId, new ReactionCounts(0, 0));
            await db.Reviews.Where(r => r.Id == reviewId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Likes, counts.Likes).SetProperty(r => r.Dislikes, counts.Dislikes));
            await transaction.CommitAsync();
            var mine = removed == 0 ? (isLike ? Like : Dislike) : null;
            return new ReviewResult<ReactionResult>(StatusCodes.Status200OK, "", new ReactionResult(counts.Likes, counts.Dislikes, mine));
        });
    }

    // Older clients' POST /api/ratings: sets the user's rating for a bus by updating their latest review of it, or
    // starting a rating-only review when they have none.
    public async Task<ReviewResult<RatingSummaryViewModel>> SetRatingAsync(Guid userId, Guid busId, int? rating, string? comment)
    {
        if (!ValidRating(rating))
        {
            return new(StatusCodes.Status400BadRequest, "A rating must be from 1 to 5 stars.");
        }
        if (!await db.Buses.AnyAsync(b => b.Id == busId))
        {
            return new(StatusCodes.Status404NotFound, "Bus not found.");
        }
        var user = await db.Users.FindAsync(userId);
        if (user is null)
        {
            return new(StatusCodes.Status401Unauthorized, "Please sign in to continue.");
        }
        var now = DateTime.UtcNow;
        var review = await db.Reviews.Where(r => r.BusId == busId && r.UserId == userId).OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync();
        if (review is null)
        {
            review = new Review { BusId = busId, UserId = userId, UserName = user.Name, Comment = comment?.Trim() ?? "", CreatedAt = now };
            db.Reviews.Add(review);
        }
        review.Rating = rating;
        review.UpdatedAt = now;
        await db.SaveChangesAsync();
        return new(StatusCodes.Status200OK, "Rating saved.", await SummaryAsync(busId));
    }
}
