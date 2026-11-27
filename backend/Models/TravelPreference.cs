using System.ComponentModel.DataAnnotations;

namespace SmartCommute.Server.Models;

// F19: a commuter's saved journeys ("Home → University", "Home → Office", …). A user may keep many; each one is its
// own row and belongs to exactly one user.

public static class TravelBusTypes
{
    public const string Ac = "AC";
    public const string NonAc = "Non-AC";
    public const string Both = "Both";

    public static readonly string[] All = [Ac, NonAc, Both];
}

public static class TravelPurposes
{
    public static readonly string[] All = ["University", "Office", "Coaching", "Shopping", "Medical", "Personal", "Other"];
}

public static class TravelWeekDays
{
    // Saturday first, as the Bangladeshi week starts.
    public static readonly string[] All = ["Saturday", "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday"];
}

public class TravelPreference
{
    public const int MaxPerUser = 20;

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(60)]
    public string PreferenceName { get; set; } = "";

    [MaxLength(100)]
    public string StartLocation { get; set; } = "";

    [MaxLength(100)]
    public string Destination { get; set; } = "";

    // The Dhaka route the user picked (each bus row is one route). Its name and number are copied in, so the
    // preference keeps them if the route is later edited or removed.
    public Guid? BusId { get; set; }
    public Bus? Bus { get; set; }

    [MaxLength(100)]
    public string FrequentlyUsedBus { get; set; } = "";

    [MaxLength(30)]
    public string? BusRouteNumber { get; set; }

    public TimeOnly PreferredTravelTime { get; set; }

    // How many days a week the user makes this journey, 1 to 7.
    public int WeeklyFrequency { get; set; }

    // Day names from TravelWeekDays.All, in week order (a PostgreSQL text[]).
    public List<string> TravelDays { get; set; } = [];

    [MaxLength(10)]
    public string BusType { get; set; } = TravelBusTypes.Both;

    [MaxLength(20)]
    public string TravelPurpose { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
