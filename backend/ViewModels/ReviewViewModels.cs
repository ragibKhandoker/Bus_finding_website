using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
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
