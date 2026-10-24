using System.ComponentModel.DataAnnotations;

namespace SmartCommute.Server.Models;

public static class RideStatus
{
    public const string Active = "active";
    public const string Completed = "completed";
}

public static class RideRequestStatus
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
}

public class RidePost
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(100)]
    public string UserName { get; set; } = "";

    [MaxLength(30)]
    public string Gender { get; set; } = "";

    [MaxLength(200)]
    public string Source { get; set; } = "";

    [MaxLength(200)]
    public string Destination { get; set; } = "";

    // Includes the host.
    public int MaxParticipants { get; set; } = 4;

    public double? Distance { get; set; }
    public decimal? Fare { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = RideStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<RideRequest> Requests { get; set; } = [];
}

public class RideRequest
{
    public Guid Id { get; set; }
    public Guid RidePostId { get; set; }
    public RidePost? RidePost { get; set; }
    public Guid RequesterId { get; set; }
    public User? Requester { get; set; }

    [MaxLength(100)]
    public string RequesterName { get; set; } = "";

    [MaxLength(30)]
    public string RequesterGender { get; set; } = "";

    [MaxLength(20)]
    public string Status { get; set; } = RideRequestStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
