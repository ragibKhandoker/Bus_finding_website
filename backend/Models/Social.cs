using System.ComponentModel.DataAnnotations;

namespace SmartCommute.Server.Models;

public static class NotificationTypes
{
    public const string Info = "info";
    public const string Success = "success";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Sos = "sos";
}

// What a notification is about, so the page can show a fitting icon and filter by kind.
public static class NotificationCategories
{
    public const string General = "general";
    public const string Account = "account";
    public const string Friend = "friend";
    public const string Ride = "ride";
    public const string Reward = "reward";
    public const string Sos = "sos";

    public static readonly string[] All = [General, Account, Friend, Ride, Reward, Sos];
}

public class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(150)]
    public string Title { get; set; } = "";

    [MaxLength(1000)]
    public string Message { get; set; } = "";

    // How it should look (info, success, warning, error, sos).
    [MaxLength(20)]
    public string Type { get; set; } = NotificationTypes.Info;

    [MaxLength(20)]
    public string Category { get; set; } = NotificationCategories.General;

    // Who caused it (a friend, a rider, the sender of an SOS), when someone did.
    public Guid? SenderId { get; set; }
    public User? Sender { get; set; }

    // The friend request, ride, review or trip it is about, when there is one.
    public Guid? RelatedId { get; set; }

    public bool IsRead { get; set; }

    [MaxLength(500)]
    public string? ActionUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// A one-to-one conversation. User1Id is always the smaller id so each pair has exactly one chat.
// A private conversation between two users, stored once per pair with the smaller id first (the same order as
// Friendship.Order), so the unique index on the pair stops a second conversation being created.
public class Chat
{
    public Guid Id { get; set; }
    public Guid User1Id { get; set; }
    public User? User1 { get; set; }
    public Guid User2Id { get; set; }
    public User? User2 { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastMessageAt { get; set; }

    public List<ChatMessage> Messages { get; set; } = [];

    public Guid OtherParticipant(Guid userId) => User1Id == userId ? User2Id : User1Id;
    public User? OtherUser(Guid userId) => User1Id == userId ? User2 : User1;
    public bool HasParticipant(Guid userId) => User1Id == userId || User2Id == userId;
}

public class ChatMessage
{
    public Guid Id { get; set; }
    public Guid ChatId { get; set; }
    public Chat? Chat { get; set; }
    public Guid SenderId { get; set; }
    public User? Sender { get; set; }

    public const int MaxLength = 2000;

    [MaxLength(MaxLength)]
    public string Text { get; set; } = "";

    // Read receipts are per message: set by the recipient (never the sender) through POST /api/chat/{id}/read.
    public bool Read { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class VerificationStatus
{
    public const string Hold = "hold";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

public class Verification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(100)]
    public string UserName { get; set; } = "";

    [MaxLength(255)]
    public string UserEmail { get; set; } = "";

    [MaxLength(200)]
    public string InstitutionName { get; set; } = "";

    [MaxLength(100)]
    public string InstitutionId { get; set; } = "";

    [MaxLength(255)]
    public string Gmail { get; set; } = "";

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = VerificationStatus.Hold;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
}
