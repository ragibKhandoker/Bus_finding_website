using System.ComponentModel.DataAnnotations;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.ViewModels;

public class FavoriteBusViewModel : EntityViewModel
{
    public string UserId { get; init; } = "";
    public string BusId { get; init; } = "";
    public string BusName { get; init; } = "";
    public string? RouteNumber { get; init; }
    public string? Operator { get; init; }
    public DateTime CreatedAt { get; init; }

    public static FavoriteBusViewModel From(FavoriteBus f) => new()
    {
        Id = f.Id.ToString(),
        UserId = f.UserId.ToString(),
        BusId = f.BusId.ToString(),
        BusName = f.BusName,
        RouteNumber = f.RouteNumber,
        Operator = f.Operator,
        CreatedAt = f.CreatedAt,
    };
}

public class FavoriteBusRequest
{
    public Guid? UserId { get; set; }

    [Required]
    public Guid? BusId { get; set; }

    [StringLength(100)]
    public string? BusName { get; set; }

    [StringLength(30)]
    public string? RouteNumber { get; set; }

    [StringLength(100)]
    public string? Operator { get; set; }
}

public class ReplyViewModel : EntityViewModel
{
    public string UserId { get; init; } = "";
    public string UserName { get; init; } = "";
    public string Comment { get; init; } = "";
    public int Likes { get; init; }
    public int Dislikes { get; init; }
    public DateTime CreatedAt { get; init; }

    public static ReplyViewModel From(ReviewReply r) => new()
    {
        Id = r.Id.ToString(),
        UserId = r.UserId.ToString(),
        UserName = r.UserName,
        Comment = r.Comment,
        Likes = r.Likes,
        Dislikes = r.Dislikes,
        CreatedAt = r.CreatedAt,
    };
}

public class ReviewViewModel : EntityViewModel
{
    public string BusId { get; init; } = "";
    public string UserId { get; init; } = "";
    public string UserName { get; init; } = "";
    public string Comment { get; init; } = "";
    public int Likes { get; init; }
    public int Dislikes { get; init; }
    // Number of replies; the replies themselves are in repliesList.
    public int Replies { get; init; }
    public List<ReplyViewModel> RepliesList { get; init; } = [];
    public DateTime CreatedAt { get; init; }

    public static ReviewViewModel From(Review r) => new()
    {
        Id = r.Id.ToString(),
        BusId = r.BusId.ToString(),
        UserId = r.UserId.ToString(),
        UserName = r.UserName,
        Comment = r.Comment,
        Likes = r.Likes,
        Dislikes = r.Dislikes,
        Replies = r.Replies.Count,
        RepliesList = r.Replies.OrderBy(x => x.CreatedAt).Select(ReplyViewModel.From).ToList(),
        CreatedAt = r.CreatedAt,
    };
}

public class CreateReviewRequest
{
    [Required]
    public Guid? BusId { get; set; }

    [Required]
    public Guid? UserId { get; set; }

    [StringLength(100)]
    public string? UserName { get; set; }

    [Required, StringLength(1000, MinimumLength = 1)]
    public string Comment { get; set; } = "";
}

public class ReplyRequest
{
    [Required]
    public Guid? UserId { get; set; }

    [StringLength(100)]
    public string? UserName { get; set; }

    [Required, StringLength(1000, MinimumLength = 1)]
    public string Comment { get; set; } = "";
}

public class UpdateReviewRequest
{
    [Required, StringLength(1000, MinimumLength = 1)]
    public string Comment { get; set; } = "";
}

public class SubmitRatingRequest
{
    [Required]
    public Guid? BusId { get; set; }

    [Required]
    public Guid? UserId { get; set; }

    [Range(1, 5)]
    public double Rating { get; set; }

    [StringLength(1000)]
    public string? Comment { get; set; }
}

public class RatingSummaryViewModel
{
    public string BusId { get; init; } = "";
    public double AverageRating { get; init; }
    public int TotalRatings { get; init; }
}
