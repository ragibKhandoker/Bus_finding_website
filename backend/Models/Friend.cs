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

    // The sender and the receiver.
    public Guid FromUserId { get; set; }
    public User? FromUser { get; set; }
    public Guid ToUserId { get; set; }
    public User? ToUser { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = FriendRequestStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // When the status last changed (equal to CreatedAt while the request is pending).
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// One row per pair of friends, stored with the smaller id first (see Order), so the database's primary key
// stops the same friendship from being stored twice and a check constraint keeps the order.
public class Friendship
{
    public Guid User1Id { get; set; }
    public User? User1 { get; set; }
    public Guid User2Id { get; set; }
    public User? User2 { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Orders two ids the way PostgreSQL orders uuid values (byte by byte, which is the order of their
    // lower-case text form). Guid.CompareTo uses a different order, so it must not be used here.
    public static (Guid User1Id, Guid User2Id) Order(Guid a, Guid b) =>
        string.CompareOrdinal(a.ToString(), b.ToString()) < 0 ? (a, b) : (b, a);
}
