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

public class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(150)]
    public string Title { get; set; } = "";

    [MaxLength(1000)]
    public string Message { get; set; } = "";

    [MaxLength(20)]
    public string Type { get; set; } = NotificationTypes.Info;

    public bool IsRead { get; set; }

    [MaxLength(500)]
    public string? ActionUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// A one-to-one conversation. User1Id is always the smaller id so each pair has exactly one chat.
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
    public bool HasParticipant(Guid userId) => User1Id == userId || User2Id == userId;
}

public class ChatMessage
{
    public Guid Id { get; set; }
    public Guid ChatId { get; set; }
    public Chat? Chat { get; set; }
    public Guid SenderId { get; set; }
    public User? Sender { get; set; }

    [MaxLength(2000)]
    public string Text { get; set; } = "";

    public bool Read { get; set; }
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
