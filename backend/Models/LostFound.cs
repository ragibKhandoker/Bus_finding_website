using System.ComponentModel.DataAnnotations;

namespace SmartCommute.Server.Models;

// F17: Lost & Found on Dhaka buses.

public static class LostFoundTypes
{
    public const string Lost = "lost";
    public const string Found = "found";

    public static readonly string[] All = [Lost, Found];
}

public static class LostFoundStatus
{
    public const string Open = "open";
    public const string UnderReview = "under-review";
    public const string Resolved = "resolved";
    public const string Closed = "closed";

    public static readonly string[] All = [Open, UnderReview, Resolved, Closed];

    // What the person who made a report may set it to themselves; only admins put a report under review.
    public static readonly string[] ForOwner = [Open, Resolved, Closed];
}

public static class LostFoundCategories
{
    public static readonly string[] All = ["Mobile Phone", "Wallet", "Bag", "Documents", "Keys", "Electronics", "Clothing", "Other"];
}

// One lost or found item report. Lost and found reports share this table; ReportType tells them apart. BusName and
// Route are copied from the Dhaka route the user picked, so the report keeps them even if the route changes later.
public class LostFound
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(10)]
    public string ReportType { get; set; } = LostFoundTypes.Lost;

    [MaxLength(100)]
    public string ItemName { get; set; } = "";

    [MaxLength(30)]
    public string Category { get; set; } = "Other";

    // The Dhaka route picked from the bus list (null if that route was later removed).
    public Guid? BusId { get; set; }

    [MaxLength(100)]
    public string BusName { get; set; } = "";

    [MaxLength(160)]
    public string Route { get; set; } = "";

    [MaxLength(200)]
    public string Location { get; set; } = "";

    // Dhaka date and time of the loss or find; the time is optional because people often do not know it.
    public DateOnly IncidentDate { get; set; }
    public TimeOnly? IncidentTime { get; set; }

    [MaxLength(1000)]
    public string Description { get; set; } = "";

    // The stored picture (a random file name under LostFound:StoragePath); only the name is kept here.
    [MaxLength(50)]
    public string? ImageKey { get; set; }

    [MaxLength(20)]
    public string? ImageContentType { get; set; }

    [MaxLength(150)]
    public string ContactInformation { get; set; } = "";

    [MaxLength(20)]
    public string Status { get; set; } = LostFoundStatus.Open;

    // A note from an admin who reviewed the report.
    [MaxLength(300)]
    public string? AdminNote { get; set; }

    public DateTime? ResolvedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// A private conversation about one report, between the person who made it (Owner) and one other user who wants to
// return or claim the item (Other). There is at most one per report and person.
public class LostFoundConversation
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public LostFound? Report { get; set; }
    public Guid OwnerId { get; set; }
    public User? Owner { get; set; }
    public Guid OtherUserId { get; set; }
    public User? OtherUser { get; set; }

    // When each side last read the conversation; messages after it are unread for them.
    public DateTime? OwnerReadAt { get; set; }
    public DateTime? OtherReadAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

    public List<LostFoundMessage> Messages { get; set; } = [];
}

public class LostFoundMessage
{
    public const int MaxLength = 1000;

    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public LostFoundConversation? Conversation { get; set; }
    public Guid SenderId { get; set; }

    // May be empty when the message is only a picture.
    [MaxLength(MaxLength)]
    public string Text { get; set; } = "";

    // A picture sent in the chat (a screenshot or photo), stored like report pictures; only the file name is kept.
    [MaxLength(50)]
    public string? ImageKey { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<LostFoundMessageReaction> Reactions { get; set; } = [];
}

// One person's reaction to a message. Each of the two people can have one reaction per message.
public class LostFoundMessageReaction
{
    public static readonly string[] Allowed = ["👍", "❤️", "😂", "😮", "😢", "🙏"];

    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public LostFoundMessage? Message { get; set; }
    public Guid UserId { get; set; }

    [MaxLength(16)]
    public string Emoji { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
