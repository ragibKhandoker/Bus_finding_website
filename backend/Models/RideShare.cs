using System.ComponentModel.DataAnnotations;

namespace SmartCommute.Server.Models;

// "active" is what the product calls an Open ride; the stored value is kept for existing data and clients.
public static class RideStatus
{
    public const string Active = "active";
    public const string Completed = "completed";
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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // When the host accepted or rejected the request (equal to CreatedAt while it is pending).
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
