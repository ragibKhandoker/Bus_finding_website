using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// Bus reviews. Anyone may read them; writing, editing, deleting, replying and reacting need a signed-in user, who
// is always taken from the token (a userId in the body is ignored).
[Route("api/reviews")]
public class ReviewsController(ReviewService reviews) : ApiControllerBase
{
    // A bus's reviews, newest first, with replies, reaction counts, the reader's own reaction (when signed in) and
    // the rating summary: ?page=1&pageSize=20 (pageSize at most 50).
    [HttpGet("bus/{busId:guid}")]
    public async Task<IActionResult> ForBus(Guid busId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var (list, total) = await reviews.ForBusAsync(busId, IsSignedIn ? CurrentUserId : null, page, pageSize);
        return Ok(new
        {
            success = true,
            count = list.Count,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling(total / (double)pageSize),
            summary = await reviews.SummaryAsync(busId),
            reviews = list,
        });
    }

    [Authorize]
    [HttpPost("")]
    public async Task<IActionResult> Create(CreateReviewRequest request)
    {
        var result = await reviews.CreateAsync(CurrentUserId, request.BusId!.Value, request.Rating, request.Comment);
        return result.Succeeded
            ? Created201(new { success = true, message = result.Message, review = result.Value, summary = await reviews.SummaryAsync(request.BusId.Value) })
            : Error(result.StatusCode, result.Message);
    }

    // Author only.
    [Authorize]
    [HttpPut("{reviewId:guid}")]
    public async Task<IActionResult> Update(Guid reviewId, UpdateReviewRequest request)
    {
        var result = await reviews.UpdateAsync(CurrentUserId, reviewId, request.Rating, request.Comment);
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, review = result.Value, summary = await reviews.SummaryAsync(Guid.Parse(result.Value!.BusId)) })
            : Error(result.StatusCode, result.Message);
    }

    // The author, or an admin removing an inappropriate review. Its replies and reactions go with it.
    [Authorize]
    [HttpDelete("{reviewId:guid}")]
    public async Task<IActionResult> Delete(Guid reviewId)
    {
        var result = await reviews.DeleteAsync(CurrentUserId, IsAdmin, reviewId);
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, summary = await reviews.SummaryAsync(result.Value) })
            : Error(result.StatusCode, result.Message);
    }

    [Authorize]
    [HttpPost("{reviewId:guid}/reply")]
    public async Task<IActionResult> Reply(Guid reviewId, ReplyRequest request)
    {
        var result = await reviews.ReplyAsync(CurrentUserId, reviewId, request.BusId, request.Comment);
        return result.Succeeded
            ? Created201(new { success = true, message = result.Message, reply = result.Value })
            : Error(result.StatusCode, result.Message);
    }

    [Authorize]
    [HttpDelete("replies/{replyId:guid}")]
    public async Task<IActionResult> DeleteReply(Guid replyId)
    {
        var result = await reviews.DeleteReplyAsync(CurrentUserId, IsAdmin, replyId);
        return result.Succeeded ? Ok(new { success = true, message = result.Message }) : Error(result.StatusCode, result.Message);
    }

    // Toggle: liking a review you already like removes the like; liking one you dislike switches it.
    [Authorize]
    [HttpPost("{reviewId:guid}/like")]
    public Task<IActionResult> Like(Guid reviewId) => React(reviewId, isLike: true);

    [Authorize]
    [HttpPost("{reviewId:guid}/dislike")]
    public Task<IActionResult> Dislike(Guid reviewId) => React(reviewId, isLike: false);

    private async Task<IActionResult> React(Guid reviewId, bool isLike)
    {
        var result = await reviews.ReactAsync(CurrentUserId, reviewId, isLike);
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        var value = result.Value!;
        return Ok(new { success = true, likes = value.Likes, dislikes = value.Dislikes, myReaction = value.MyReaction });
    }
}

// Rating summaries, worked out from the stars on reviews.
[Route("api/ratings")]
public class RatingsController(ReviewService reviews) : ApiControllerBase
{
    [HttpGet("bus/{busId:guid}")]
    public async Task<IActionResult> ForBus(Guid busId) => Ok(new { success = true, data = await reviews.SummaryAsync(busId) });

    // Keyed by bus id, which is the shape the Flutter map screen expects.
    [HttpGet("all")]
    public async Task<IActionResult> All()
    {
        var data = (await reviews.AllSummariesAsync()).ToDictionary(
            g => g.BusId.ToString(),
            g => new RatingSummaryViewModel { BusId = g.BusId.ToString(), AverageRating = Math.Round(g.Average, 1), TotalRatings = g.Count });
        return Ok(new { success = true, data });
    }

    // For older clients: sets the signed-in user's rating for a bus (see ReviewService.SetRatingAsync).
    [Authorize]
    [HttpPost("")]
    public async Task<IActionResult> Submit(SubmitRatingRequest request)
    {
        var result = await reviews.SetRatingAsync(CurrentUserId, request.BusId!.Value, request.Rating, request.Comment);
        return result.Succeeded ? Ok(new { success = true, message = result.Message, data = result.Value }) : Error(result.StatusCode, result.Message);
    }
}
