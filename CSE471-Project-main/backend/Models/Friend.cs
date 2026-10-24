using System.ComponentModel.DataAnnotations;

namespace SmartCommute.Server.Models;

public static class FriendRequestStatus
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
}

public class FriendRequest
{
    public Guid Id { get; set; }
    public Guid FromUserId { get; set; }
    public User? FromUser { get; set; }
    public Guid ToUserId { get; set; }
    public User? ToUser { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = FriendRequestStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// One row per direction, so a friendship between A and B is stored as (A, B) and (B, A).
public class Friendship
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid FriendId { get; set; }
    public User? Friend { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
