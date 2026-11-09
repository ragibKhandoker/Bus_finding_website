using System.ComponentModel.DataAnnotations;

namespace SmartCommute.Server.Models;

// A ride's lifecycle. "active" is what the product calls an OPEN ride; the stored value is kept for existing data
// and clients. OPEN -> FULL when the last seat is taken (and back when one frees up); OPEN/FULL -> COMPLETED or
// CANCELLED by the host; OPEN/FULL -> EXPIRED by RideLifecycle once the departure has passed.
public static class RideStatus
{
    public const string Active = "active";
    public const string Full = "full";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";

    public static readonly string[] All = [Active, Full, Completed, Cancelled, Expired];

    // Rides that are still going ahead: the host can edit or cancel them and riders can talk in the ride chat.
    public static bool IsLive(string status) => status is Active or Full;

    // The name the product uses: OPEN, FULL, COMPLETED, CANCELLED, EXPIRED.
    public static string Label(string status) => status == Active ? "OPEN" : status.ToUpperInvariant();
}

public static class RideVehicleTypes
{
    public const string Car = "Car";
    public const string Bike = "Bike";
    public const string Cng = "CNG";
    public const string Other = "Other";

    public static readonly string[] All = [Car, Bike, Cng, Other];

    public static string? Normalize(string? value) =>
        All.FirstOrDefault(v => string.Equals(v, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    // The most people (counting the host) the vehicle can take: a bike carries one passenger, a CNG three.
    public static int Capacity(string vehicle, int configuredMax) => vehicle switch
    {
        Bike => 2,
        Cng => Math.Min(4, configuredMax),
        _ => configuredMax,
    };
}

// How riders may reach the host. The host's phone is only ever shown to the host and approved riders.
public static class RideContactPreferences
{
    public const string Chat = "chat";
    public const string Phone = "phone";
    public const string Both = "both";

    public static readonly string[] All = [Chat, Phone, Both];

    public static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? Chat
        : All.FirstOrDefault(v => string.Equals(v, value.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool SharesPhone(string preference) => preference is Phone or Both;
}

// Who the host would like to share the ride with.
public static class RideGenderPreference
{
    public const string Any = "Any";
    public const string Male = "Male";
    public const string Female = "Female";

    public static readonly string[] All = [Any, Male, Female];

    // Matches case-insensitively and returns the stored spelling, or null if the value is not allowed.
    public static string? Normalize(string? value) =>
        All.FirstOrDefault(g => string.Equals(g, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    // Whether someone of this gender (a UserGenders value) may join a ride with this preference.
    public static bool Allows(string preference, string gender) =>
        preference == Any || string.Equals(preference, gender, StringComparison.OrdinalIgnoreCase);
}

// "accepted" is what the product calls APPROVED. A request is CANCELLED when the rider withdraws it, when the host
// removes an approved rider (RemovedByHost), or when the ride closes while it is still pending.
public static class RideRequestStatus
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";

    public static string Label(string status) => status == Accepted ? "APPROVED" : status.ToUpperInvariant();
}

public class RidePost
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(100)]
    public string UserName { get; set; } = "";

    // The host's own gender when the post was made.
    [MaxLength(30)]
    public string Gender { get; set; } = "";

    [MaxLength(10)]
    public string GenderPreference { get; set; } = RideGenderPreference.Any;

    [MaxLength(200)]
    public string Source { get; set; } = "";

    [MaxLength(200)]
    public string Destination { get; set; } = "";

    // Includes the host.
    public int MaxParticipants { get; set; } = 4;

    public double? Distance { get; set; }
    public decimal? Fare { get; set; }

    // Where the ride starts and ends. New rides always have coordinates inside Dhaka City; rides posted before
    // locations were checked may not.
    public double? SourceLat { get; set; }
    public double? SourceLng { get; set; }
    public double? DestinationLat { get; set; }
    public double? DestinationLng { get; set; }

    // When the ride leaves, in UTC (typed and shown in Dhaka time). Null only for rides posted before it was asked.
    public DateTime? DepartureAt { get; set; }

    [MaxLength(10)]
    public string VehicleType { get; set; } = RideVehicleTypes.Other;

    // What each rider is expected to pay, in taka.
    public decimal? CostPerPerson { get; set; }

    [MaxLength(500)]
    public string? Instructions { get; set; }

    [MaxLength(10)]
    public string ContactPreference { get; set; } = RideContactPreferences.Chat;

    // Where exactly to meet. Shown only to the host and approved riders.
    [MaxLength(300)]
    public string? PickupDetails { get; set; }

    public DateTime? CancelledAt { get; set; }

    [MaxLength(300)]
    public string? CancelReason { get; set; }

    public DateTime? ExpiredAt { get; set; }

    // Set once the "ride leaves soon" reminder went out, so it is sent only once.
    public DateTime? ReminderSentAt { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = RideStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    // Set when the host deletes the post. Deleted posts are left out of every list and lookup.
    public DateTime? DeletedAt { get; set; }

    public List<RideRequest> Requests { get; set; } = [];
}

// A request to join a ride. The ride's participants are its accepted requests plus the host; there is no
// separate participant table.
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

    // An optional note to the host, such as where the rider will wait.
    [MaxLength(200)]
    public string? Note { get; set; }

    // True when the host removed this approved rider (Status is then Cancelled).
    public bool RemovedByHost { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // When the request last changed: approved, rejected or cancelled (equal to CreatedAt while it is pending).
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// A message in a ride's private group chat. Only the host and approved riders can read or write them.
public class RideMessage
{
    public const int MaxLength = 1000;

    public Guid Id { get; set; }
    public Guid RidePostId { get; set; }
    public RidePost? RidePost { get; set; }
    public Guid SenderId { get; set; }
    public User? Sender { get; set; }

    [MaxLength(MaxLength)]
    public string Text { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// One person's review of another after a completed ride they were both on (host or approved rider).
// One review per reviewer, person and ride.
public class RideReview
{
    public const int MaxCommentLength = 1000;

    public Guid Id { get; set; }
    public Guid RidePostId { get; set; }
    public RidePost? RidePost { get; set; }
    public Guid ReviewerId { get; set; }
    public User? Reviewer { get; set; }
    public Guid ReviewedUserId { get; set; }
    public User? ReviewedUser { get; set; }

    // 1 to 5 stars.
    public int Rating { get; set; }

    [MaxLength(MaxCommentLength)]
    public string Comment { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class RideReportReasons
{
    public static readonly string[] All = ["unsafe", "harassment", "fake", "spam", "no_show", "other"];
    public static string? Normalize(string? value) => All.FirstOrDefault(r => string.Equals(r, value?.Trim(), StringComparison.OrdinalIgnoreCase));
}

public static class RideReportStatus
{
    public const string Open = "open";
    public const string Reviewed = "reviewed";
    public const string Dismissed = "dismissed";
}

// A report about a ride (then ReportedUserId is its host) or about a person, for admins to review.
public class RideReport
{
    public Guid Id { get; set; }
    public Guid? RidePostId { get; set; }
    public RidePost? RidePost { get; set; }
    public Guid ReporterId { get; set; }
    public User? Reporter { get; set; }
    public Guid? ReportedUserId { get; set; }
    public User? ReportedUser { get; set; }

    [MaxLength(20)]
    public string Reason { get; set; } = "other";

    [MaxLength(1000)]
    public string Description { get; set; } = "";

    [MaxLength(20)]
    public string Status { get; set; } = RideReportStatus.Open;

    [MaxLength(500)]
    public string? AdminNote { get; set; }

    public Guid? ReviewedById { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// BlockerId no longer sees BlockedId's rides, and neither can ask to join the other's rides.
public class UserBlock
{
    public Guid Id { get; set; }
    public Guid BlockerId { get; set; }
    public User? Blocker { get; set; }
    public Guid BlockedId { get; set; }
    public User? Blocked { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
