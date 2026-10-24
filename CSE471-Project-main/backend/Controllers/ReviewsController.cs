using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// Called without a token by the existing clients; see CanActAs for how a token is honoured when present.
[Route("api/reviews")]
public class ReviewsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet("bus/{busId:guid}")]
    public async Task<IActionResult> ForBus(Guid busId)
    {
        var reviews = await db.Reviews
            .Include(r => r.Replies)
            .Where(r => r.BusId == busId)
            .OrderByDescending(r => r.CreatedAt)
            .AsNoTracking()
            .ToListAsync();
        return Ok(new { success = true, count = reviews.Count, reviews = reviews.Select(ReviewViewModel.From) });
    }

    [HttpPost("")]
    public async Task<IActionResult> Create(CreateReviewRequest request)
    {
        var user = await db.Users.FindAsync(request.UserId);
        if (user is null || !await db.Buses.AnyAsync(b => b.Id == request.BusId))
        {
            return NotFoundError(user is null ? "User not found." : "Bus not found.");
        }
        if (!CanActAs(user.Id))
        {
            return ForbiddenError();
        }

        var review = new Review
        {
            BusId = request.BusId!.Value,
            UserId = user.Id,
            UserName = string.IsNullOrWhiteSpace(request.UserName) ? user.Name : request.UserName.Trim(),
            Comment = request.Comment.Trim(),
        };
        db.Reviews.Add(review);
        await db.SaveChangesAsync();
        return Created201(new { success = true, message = "Review posted.", review = ReviewViewModel.From(review) });
    }

    [HttpPost("{reviewId:guid}/like")]
    public Task<IActionResult> Like(Guid reviewId, UserIdRequest request) => React(reviewId, request.UserId!.Value, isLike: true);

    [HttpPost("{reviewId:guid}/dislike")]
    public Task<IActionResult> Dislike(Guid reviewId, UserIdRequest request) => React(reviewId, request.UserId!.Value, isLike: false);

    [HttpPost("{reviewId:guid}/reply")]
    public async Task<IActionResult> Reply(Guid reviewId, ReplyRequest request)
    {
        var review = await db.Reviews.FindAsync(reviewId);
        var user = await db.Users.FindAsync(request.UserId);
        if (review is null || user is null)
        {
            return NotFoundError(review is null ? "Review not found." : "User not found.");
        }
        if (!CanActAs(user.Id))
        {
            return ForbiddenError();
        }

        var reply = new ReviewReply
        {
            ReviewId = review.Id,
            UserId = user.Id,
            UserName = string.IsNullOrWhiteSpace(request.UserName) ? user.Name : request.UserName.Trim(),
            Comment = request.Comment.Trim(),
        };
        db.ReviewReplies.Add(reply);
        await db.SaveChangesAsync();
        return Ok(new { success = true, message = "Reply added.", reply = ReplyViewModel.From(reply) });
    }

    [HttpPut("{reviewId:guid}")]
    public async Task<IActionResult> Update(Guid reviewId, UpdateReviewRequest request)
    {
        var review = await db.Reviews.Include(r => r.Replies).FirstOrDefaultAsync(r => r.Id == reviewId);
        if (review is null)
        {
            return NotFoundError("Review not found.");
        }
        if (!CanActAs(review.UserId))
        {
            return ForbiddenError("You can only edit your own review.");
        }

        review.Comment = request.Comment.Trim();
        review.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { success = true, message = "Review updated.", review = ReviewViewModel.From(review) });
    }

    [HttpDelete("{reviewId:guid}")]
    public async Task<IActionResult> Delete(Guid reviewId)
    {
        var review = await db.Reviews.FindAsync(reviewId);
        if (review is null)
        {
            return NotFoundError("Review not found.");
        }
        if (!CanActAs(review.UserId))
        {
            return ForbiddenError("You can only delete your own review.");
        }

        db.Reviews.Remove(review);
        await db.SaveChangesAsync();
        return Ok(new { success = true, message = "Review deleted." });
    }

    // Reacting again with the same choice removes it; switching replaces the old reaction.
    private async Task<IActionResult> React(Guid reviewId, Guid userId, bool isLike)
    {
        var review = await db.Reviews.FindAsync(reviewId);
        if (review is null)
        {
            return NotFoundError("Review not found.");
        }
        if (!await db.Users.AnyAsync(u => u.Id == userId))
        {
            return NotFoundError("User not found.");
        }
        if (!CanActAs(userId))
        {
            return ForbiddenError();
        }

        var existing = await db.ReviewReactions.FindAsync(reviewId, userId);
        if (existing is null)
        {
            db.ReviewReactions.Add(new ReviewReaction { ReviewId = reviewId, UserId = userId, IsLike = isLike });
        }
        else if (existing.IsLike == isLike)
        {
            db.ReviewReactions.Remove(existing);
        }
        else
        {
            existing.IsLike = isLike;
        }
        await db.SaveChangesAsync();

        review.Likes = await db.ReviewReactions.CountAsync(r => r.ReviewId == reviewId && r.IsLike);
        review.Dislikes = await db.ReviewReactions.CountAsync(r => r.ReviewId == reviewId && !r.IsLike);
        await db.SaveChangesAsync();
        return Ok(new { success = true, likes = review.Likes, dislikes = review.Dislikes });
    }
}

[Route("api/ratings")]
public class RatingsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet("bus/{busId:guid}")]
    public async Task<IActionResult> ForBus(Guid busId)
    {
        var values = await db.Ratings.Where(r => r.BusId == busId).Select(r => r.Value).ToListAsync();
        return Ok(new { success = true, data = Summary(busId, values) });
    }

    // Keyed by bus id, which is the shape the Flutter map screen expects.
    [HttpGet("all")]
    public async Task<IActionResult> All()
    {
        var groups = await db.Ratings
            .GroupBy(r => r.BusId)
            .Select(g => new { BusId = g.Key, Average = g.Average(r => r.Value), Count = g.Count() })
            .ToListAsync();
        var data = groups.ToDictionary(
            g => g.BusId.ToString(),
            g => new RatingSummaryViewModel { BusId = g.BusId.ToString(), AverageRating = Math.Round(g.Average, 1), TotalRatings = g.Count });
        return Ok(new { success = true, data });
    }

    // One rating per user per bus; submitting again replaces the earlier rating.
    [HttpPost("")]
    public async Task<IActionResult> Submit(SubmitRatingRequest request)
    {
        var busId = request.BusId!.Value;
        var userId = request.UserId!.Value;
        if (!await db.Buses.AnyAsync(b => b.Id == busId) || !await db.Users.AnyAsync(u => u.Id == userId))
        {
            return NotFoundError("Bus or user not found.");
        }
        if (!CanActAs(userId))
        {
            return ForbiddenError();
        }

        var rating = await db.Ratings.FirstOrDefaultAsync(r => r.BusId == busId && r.UserId == userId);
        if (rating is null)
        {
            rating = new Rating { BusId = busId, UserId = userId };
            db.Ratings.Add(rating);
        }
        rating.Value = request.Rating;
        rating.Comment = request.Comment?.Trim() ?? "";
        rating.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var values = await db.Ratings.Where(r => r.BusId == busId).Select(r => r.Value).ToListAsync();
        return Ok(new { success = true, message = "Rating saved.", data = Summary(busId, values) });
    }

    private static RatingSummaryViewModel Summary(Guid busId, List<double> values) => new()
    {
        BusId = busId.ToString(),
        AverageRating = values.Count == 0 ? 0 : Math.Round(values.Average(), 1),
        TotalRatings = values.Count,
    };
}
