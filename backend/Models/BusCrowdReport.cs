using System.ComponentModel.DataAnnotations;

namespace SmartCommute.Server.Models;

// F19: how crowded a Dhaka bus is and whether it has seats, as seen and reported by a commuter.

public static class CrowdLevels
{
    public const string Low = "LOW";
    public const string Medium = "MEDIUM";
    public const string High = "HIGH";

    public static readonly string[] All = [Low, Medium, High];
}

public static class SeatAvailabilities
{
    public const string Available = "AVAILABLE";
    public const string Limited = "LIMITED";
    public const string NoSeats = "NO_SEATS";

    public static readonly string[] All = [Available, Limited, NoSeats];
}

public static class TravelDirections
{
    // Along the stored stop order (towards the last stop) or back towards the first stop.
    public const string Forward = "FORWARD";
    public const string Return = "RETURN";

    public static readonly string[] All = [Forward, Return];
}

// How old a report is, from its observation time. Only reports up to an hour old describe the bus as it may be now.
public static class CrowdFreshness
{
    public const string VeryRecent = "VERY_RECENT";
    public const string Recent = "RECENT";
    public const string Aging = "AGING";
    public const string Expired = "EXPIRED";

    public static readonly string[] All = [VeryRecent, Recent, Aging, Expired];

    public const int VeryRecentMinutes = 15;
    public const int RecentMinutes = 30;
    public const int CurrentMinutes = 60;

    public static string Of(DateTime observedUtc, DateTime nowUtc)
    {
        var age = (nowUtc - observedUtc).TotalMinutes;
        return age <= VeryRecentMinutes ? VeryRecent : age <= RecentMinutes ? Recent : age <= CurrentMinutes ? Aging : Expired;
    }
}

// One report. BusId is the Dhaka route (in this project each bus row is one route), BusStopId a stop on it. The bus
// name, route and stop names are copied in so a report reads the same if the route is later edited.
public class BusCrowdReport
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid BusId { get; set; }
    public Bus? Bus { get; set; }

    public Guid BusStopId { get; set; }
    public BusStop? BusStop { get; set; }

    [MaxLength(100)]
    public string BusName { get; set; } = "";

    [MaxLength(40)]
    public string? RouteNumber { get; set; }

    [MaxLength(160)]
    public string RouteName { get; set; } = "";

    [MaxLength(100)]
    public string StopName { get; set; } = "";

    [MaxLength(10)]
    public string TravelDirection { get; set; } = TravelDirections.Forward;

    // The end of the route the bus was heading to, e.g. "Abdullahpur".
    [MaxLength(100)]
    public string TowardsStop { get; set; } = "";

    [MaxLength(10)]
    public string CrowdLevel { get; set; } = CrowdLevels.Low;

    [MaxLength(12)]
    public string SeatAvailability { get; set; } = SeatAvailabilities.Available;

    public int? AvailableSeatCount { get; set; }
    public int? StandingPassengerCount { get; set; }

    // When the commuter saw the bus (UTC). Freshness is always worked out from this, never from CreatedAt.
    public DateTime ObservationDateTime { get; set; }

    // The observation time in 10-minute slots since the Unix epoch. A unique index on (user, bus, stop, slot) stops the
    // same person saving the same sighting twice, even when two requests race.
    public long ObservationSlot { get; set; }

    [MaxLength(500)]
    public string? AdditionalComments { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public const int SlotMinutes = 10;

    public static long SlotOf(DateTime utc) => (long)Math.Floor((utc - DateTime.UnixEpoch).TotalMinutes / SlotMinutes);
}
