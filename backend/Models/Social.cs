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

// "hold" is the stored value for Pending, kept for existing data and clients.
public static class VerificationStatus
{
    public const string Hold = "hold";
    public const string Approved = "approved";
    public const string Rejected = "rejected";

    public static string Label(string status) => status == Hold ? "pending" : status;
}

public static class VerificationDocumentTypes
{
    public const string StudentPass = "student_pass";
    public const string Nid = "nid";

    public static readonly string[] All = [StudentPass, Nid];
}

// An uploaded verification document image. The file is kept outside the public web folder and is only served to
// its owner and admins (GET /verify/images/{id}).
public class VerificationImage
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    // File name inside the private storage folder: "<random id>.<ext>", never from the user.
    [MaxLength(100)]
    public string StorageKey { get; set; } = "";

    [MaxLength(30)]
    public string ContentType { get; set; } = "";

    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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

    [MaxLength(20)]
    public string DocumentType { get; set; } = VerificationDocumentTypes.StudentPass;

    public Guid? ImageId { get; set; }
    public VerificationImage? Image { get; set; }

    // Older submissions only: a link to a file in the old public uploads folder.
    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = VerificationStatus.Hold;

    [MaxLength(500)]
    public string? RejectionReason { get; set; }

    public Guid? ReviewedById { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
}
