using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Server.ViewModels;

public class FavoriteBusViewModel : EntityViewModel
{
    public string UserId { get; init; } = "";
    public string BusId { get; init; } = "";
    public string BusName { get; init; } = "";
    public string? RouteNumber { get; init; }
    public string? Operator { get; init; }
    public DateTime CreatedAt { get; init; }

    // Current details of the bus, filled in from the route list when it is known; the name, route number and
    // operator above are the copy saved with the favourite.
    public string? BusType { get; init; }
    public string? Frequency { get; init; }

    [JsonPropertyName("base_fare")]
    public decimal? BaseFare { get; init; }

    [JsonPropertyName("per_km_fare")]
    public decimal? PerKmFare { get; init; }

    public int? StopCount { get; init; }
    public string? FirstStop { get; init; }
    public string? LastStop { get; init; }

    public static FavoriteBusViewModel From(FavoriteBus f, BusViewModel? bus = null) => new()
    {
        Id = f.Id.ToString(),
        UserId = f.UserId.ToString(),
        BusId = f.BusId.ToString(),
        BusName = bus?.BusName ?? f.BusName,
        RouteNumber = bus?.RouteNumber ?? f.RouteNumber,
        Operator = bus?.Operator ?? f.Operator,
        CreatedAt = f.CreatedAt,
        BusType = bus?.BusType,
        Frequency = bus?.Frequency,
        BaseFare = bus?.BaseFare,
        PerKmFare = bus?.PerKmFare,
        StopCount = bus?.Stops.Count,
        FirstStop = bus?.Stops.FirstOrDefault()?.Name,
        LastStop = bus?.Stops.LastOrDefault()?.Name,
    };
}

// The bus is always the signed-in user's favourite; UserId is accepted from older clients but ignored.
public class FavoriteBusRequest
{
    public string? UserId { get; set; }

    // Read as text so a malformed id gets a clear message instead of a JSON conversion error.
    [Required(ErrorMessage = "busId is required.")]
    [RegularExpression(@"^\s*[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\s*$", ErrorMessage = "busId is not a valid bus id.")]
    public string? BusId { get; set; }

    [JsonIgnore]
    public Guid BusGuid => Guid.TryParse(BusId, out var id) ? id : Guid.Empty;

    [StringLength(100)]
    public string? BusName { get; set; }

    [StringLength(30)]
    public string? RouteNumber { get; set; }

    [StringLength(100)]
    public string? Operator { get; set; }
}

// Who wrote a review or reply: only what anyone may see.
public class ReviewAuthorViewModel
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool Verified { get; init; }

    public static ReviewAuthorViewModel From(Guid id, User? user, string fallbackName) => new()
    {
        Id = id.ToString(),
        Name = user?.Name ?? fallbackName,
        Verified = user?.Pass ?? false,
    };
}

public class ReplyViewModel : EntityViewModel
{
    public string ReviewId { get; init; } = "";
    public string UserId { get; init; } = "";
    public string UserName { get; init; } = "";
    public ReviewAuthorViewModel Author { get; init; } = new();
    public string Comment { get; init; } = "";
    public DateTime CreatedAt { get; init; }

    public static ReplyViewModel From(ReviewReply r) => new()
    {
        Id = r.Id.ToString(),
        ReviewId = r.ReviewId.ToString(),
        UserId = r.UserId.ToString(),
        UserName = r.User?.Name ?? r.UserName,
        Author = ReviewAuthorViewModel.From(r.UserId, r.User, r.UserName),
        Comment = r.Comment,
        CreatedAt = r.CreatedAt,
    };
}

// A top-level review with its replies (oldest first). Likes and Dislikes are counted from the reactions;
// MyReaction is "like", "dislike" or null for the signed-in reader (null when signed out).
public class ReviewViewModel : EntityViewModel
{
    public string BusId { get; init; } = "";
    public string UserId { get; init; } = "";
    public string UserName { get; init; } = "";
    public ReviewAuthorViewModel Author { get; init; } = new();
    public int? Rating { get; init; }
    public string Comment { get; init; } = "";
    public int Likes { get; init; }
    public int Dislikes { get; init; }
    public string? MyReaction { get; init; }
    // Number of replies; the replies themselves are in repliesList.
    public int Replies { get; init; }
    public List<ReplyViewModel> RepliesList { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public bool Edited => UpdatedAt > CreatedAt.AddSeconds(1);

    public static ReviewViewModel From(Review r, ReactionCounts? counts = null, string? myReaction = null) => new()
    {
        Id = r.Id.ToString(),
        BusId = r.BusId.ToString(),
        UserId = r.UserId.ToString(),
        UserName = r.User?.Name ?? r.UserName,
        Author = ReviewAuthorViewModel.From(r.UserId, r.User, r.UserName),
        Rating = r.Rating,
        Comment = r.Comment,
        Likes = counts?.Likes ?? r.Likes,
        Dislikes = counts?.Dislikes ?? r.Dislikes,
        MyReaction = myReaction,
        Replies = r.Replies.Count,
        RepliesList = r.Replies.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Select(ReplyViewModel.From).ToList(),
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
    };
}

// The author is the signed-in user; a userId or userName a client still sends is ignored.
public class CreateReviewRequest
{
    [Required(ErrorMessage = "Choose a bus.")]
    public Guid? BusId { get; set; }

    [Required(ErrorMessage = "Choose a rating from 1 to 5 stars.")]
    [Range(1, 5, ErrorMessage = "A rating must be from 1 to 5 stars.")]
    public int? Rating { get; set; }

    [StringLength(Review.MaxCommentLength, ErrorMessage = "A review can be at most 1000 characters.")]
    public string? Comment { get; set; }
}

// Fields left out (null) are not changed.
public class UpdateReviewRequest
{
    [Range(1, 5, ErrorMessage = "A rating must be from 1 to 5 stars.")]
    public int? Rating { get; set; }

    [StringLength(Review.MaxCommentLength, ErrorMessage = "A review can be at most 1000 characters.")]
    public string? Comment { get; set; }
}

public class ReplyRequest
{
    // Optional: when sent, it must be the bus the review is about.
    public Guid? BusId { get; set; }

    [StringLength(Review.MaxCommentLength, ErrorMessage = "A reply can be at most 1000 characters.")]
    public string? Comment { get; set; }
}

// POST /api/ratings, kept for older clients: sets the signed-in user's rating for a bus.
public class SubmitRatingRequest
{
    [Required(ErrorMessage = "Choose a bus.")]
    public Guid? BusId { get; set; }

    [Required(ErrorMessage = "Choose a rating from 1 to 5 stars.")]
    [Range(1, 5, ErrorMessage = "A rating must be from 1 to 5 stars.")]
    public int? Rating { get; set; }

    [StringLength(Review.MaxCommentLength)]
    public string? Comment { get; set; }
}

// A bus's ratings. AverageRating is null when nobody has rated it yet. Distribution maps stars ("1".."5") to counts.
public class RatingSummaryViewModel
{
    public string BusId { get; init; } = "";
    public double? AverageRating { get; init; }
    public int TotalRatings { get; init; }
    public int TotalReviews { get; init; }
    public Dictionary<string, int> Distribution { get; init; } = [];
}
