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

public class Review
{
    public Guid Id { get; set; }
    public Guid BusId { get; set; }
    public Bus? Bus { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(100)]
    public string UserName { get; set; } = "";

    [MaxLength(1000)]
    public string Comment { get; set; } = "";

    public int Likes { get; set; }
    public int Dislikes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<ReviewReply> Replies { get; set; } = [];
}

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
