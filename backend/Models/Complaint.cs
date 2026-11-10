using System.ComponentModel.DataAnnotations;

namespace SmartCommute.Server.Models;

// F18: complaints and feedback about Dhaka buses.

public static class ComplaintStatus
{
    public const string Pending = "PENDING";
    public const string Reviewing = "REVIEWING";
    public const string Resolved = "RESOLVED";
    public const string Rejected = "REJECTED";

    public static readonly string[] All = [Pending, Reviewing, Resolved, Rejected];

    // Where an admin may move a complaint from each status. Resolved and rejected complaints can be reopened.
    public static readonly Dictionary<string, string[]> Next = new()
    {
        [Pending] = [Reviewing, Rejected],
        [Reviewing] = [Resolved, Rejected, Pending],
        [Resolved] = [Reviewing],
        [Rejected] = [Reviewing],
    };

    public static bool CanMove(string from, string to) => Next.TryGetValue(from, out var next) && next.Contains(to);
}

public static class ComplaintCategories
{
    public static readonly string[] All =
    [
        "Overcharging / Extra Fare", "Staff Misbehavior", "Overcrowding", "Poor Bus Condition", "Route Violation",
        "Bus Not Stopping at Designated Stop", "Delayed Bus Service", "Safety Issues", "Harassment", "Other Service Issues",
    ];
}

// One complaint. BusId is the Dhaka route the user picked (in this project each bus row is one route); its name,
// number and ends are copied in, so the complaint keeps them if the route is later edited or removed.
public class Complaint
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid? BusId { get; set; }
    public Bus? Bus { get; set; }

    [MaxLength(100)]
    public string BusName { get; set; } = "";

    [MaxLength(40)]
    public string? RouteNumber { get; set; }

    [MaxLength(160)]
    public string RouteName { get; set; } = "";

    // A stop on that route, when the user picked one.
    [MaxLength(150)]
    public string? StopName { get; set; }

    [MaxLength(60)]
    public string Category { get; set; } = "";

    [MaxLength(150)]
    public string Subject { get; set; } = "";

    [MaxLength(2000)]
    public string Description { get; set; } = "";

    public DateOnly IncidentDate { get; set; }

    [MaxLength(200)]
    public string IncidentLocation { get; set; } = "";

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = ComplaintStatus.Pending;

    // The latest note from an admin, shown to the user; every note is also kept in the history.
    [MaxLength(1000)]
    public string? AdminNotes { get; set; }

    public Guid? ReviewedById { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }

    // Two admins changing the same complaint at once cannot both be saved.
    public uint Version { get; set; }

    public List<ComplaintEvidence> Evidence { get; set; } = [];
    public List<ComplaintHistory> History { get; set; } = [];
}

// A supporting picture. Stored as a file with a random name outside wwwroot and only ever served, through
// ComplaintController, to the person who made the complaint and to admins.
public class ComplaintEvidence
{
    public Guid Id { get; set; }
    public Guid ComplaintId { get; set; }
    public Complaint? Complaint { get; set; }

    [MaxLength(50)]
    public string StorageKey { get; set; } = "";

    [MaxLength(20)]
    public string ContentType { get; set; } = "";

    public int SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Every step of a complaint: its submission and each status change, with who made it and the note.
public class ComplaintHistory
{
    public Guid Id { get; set; }
    public Guid ComplaintId { get; set; }
    public Complaint? Complaint { get; set; }

    [MaxLength(20)]
    public string? FromStatus { get; set; }

    [MaxLength(20)]
    public string ToStatus { get; set; } = "";

    [MaxLength(1000)]
    public string? Note { get; set; }

    public Guid? ActorId { get; set; }
    public User? Actor { get; set; }

    // "user" for the submission, "admin" for a review.
    [MaxLength(10)]
    public string ActorRole { get; set; } = "user";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
