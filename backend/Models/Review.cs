using System.ComponentModel.DataAnnotations;

namespace SmartCommute.Server.Models;

public class FavoriteBus
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid BusId { get; set; }
    public Bus? Bus { get; set; }

    [MaxLength(100)]
    public string BusName { get; set; } = "";

    [MaxLength(30)]
    public string? RouteNumber { get; set; }

    [MaxLength(100)]
    public string? Operator { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// A review of a bus with its 1-5 star rating. A bus's average rating is the average of its reviews' ratings.
// Reviews written before ratings moved onto reviews have no rating (null) and are left out of the average.
public class Review
{
    public const int MaxCommentLength = 1000;

    public Guid Id { get; set; }
    public Guid BusId { get; set; }
    public Bus? Bus { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(100)]
    public string UserName { get; set; } = "";

    // 1 to 5 whole stars.
    public int? Rating { get; set; }

    [MaxLength(1000)]
    public string Comment { get; set; } = "";

    // Kept in step with review_reactions for older clients; responses count the reactions themselves.
    public int Likes { get; set; }
    public int Dislikes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<ReviewReply> Replies { get; set; } = [];
}

// A reply to a review. Replies cannot themselves be replied to.
public class ReviewReply
{
    public Guid Id { get; set; }
    public Guid ReviewId { get; set; }
    public Review? Review { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(100)]
    public string UserName { get; set; } = "";

    [MaxLength(1000)]
    public string Comment { get; set; } = "";

    public int Likes { get; set; }
    public int Dislikes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// One like or dislike per user per review; reacting again with the same choice removes it.
public class ReviewReaction
{
    public Guid ReviewId { get; set; }
    public Review? Review { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public bool IsLike { get; set; }
}

// Older star ratings (one per user per bus), from before ratings were stored on reviews. The AddReviewRatings
// migration copied them onto reviews; nothing reads or writes this table any more.
public class Rating
{
    public Guid Id { get; set; }
    public Guid BusId { get; set; }
    public Bus? Bus { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    // 1 to 5.
    public double Value { get; set; }

    [MaxLength(1000)]
    public string Comment { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
