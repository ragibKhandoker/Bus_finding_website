using System.ComponentModel.DataAnnotations;

namespace SmartCommute.Server.Models;

// F22: driver duty schedules, trip management and working status.
//
// A DriverTrip is one scheduled run of a bus route by one driver: an admin creates it (that is the driver's duty
// schedule) and the driver starts and completes it. "Schedule" and "trip" are the same row seen at different times,
// so a run is never stored twice. It is a different thing from the commuter Trip (F05), which is one passenger's
// ride; the two are not merged. Routes are the existing Bus rows (each Bus is one Dhaka route with its stops) and a
// vehicle is an existing IndividualBus.

public static class DriverTripStatus
{
    public const string Scheduled = "SCHEDULED";
    public const string InProgress = "IN_PROGRESS";
    public const string Completed = "COMPLETED";
    public const string Cancelled = "CANCELLED";
    public const string Missed = "MISSED";

    public static readonly string[] All = [Scheduled, InProgress, Completed, Cancelled, Missed];

    // Trips that still occupy the driver's (and the vehicle's) time.
    public static readonly string[] Open = [Scheduled, InProgress];
}

public static class DriverTripDirection
{
    // Along the route's stops in stored order, or back the other way.
    public const string Forward = "FORWARD";
    public const string Return = "RETURN";

    public static readonly string[] All = [Forward, Return];
}

public static class DriverWorkStatus
{
    public const string Available = "AVAILABLE";
    public const string OnDuty = "ON_DUTY";
    public const string OnTrip = "ON_TRIP";
    public const string Break = "BREAK";
    public const string OffDuty = "OFF_DUTY";
    public const string Unavailable = "UNAVAILABLE";

    public static readonly string[] All = [Available, OnDuty, OnTrip, Break, OffDuty, Unavailable];

    // What a driver may choose by hand. ON_TRIP is only ever set by starting a trip.
    public static readonly string[] Manual = [Available, OnDuty, Break, OffDuty, Unavailable];

    // A trip can be started only from one of these.
    public static readonly string[] CanStartTrip = [Available, OnDuty];
}

public class DriverTrip
{
    public Guid Id { get; set; }

    public Guid DriverId { get; set; }
    public User? Driver { get; set; }

    // The route (an existing Dhaka bus route).
    public Guid BusId { get; set; }
    public Bus? Bus { get; set; }

    // The vehicle, when the admin named one.
    public Guid? IndividualBusId { get; set; }
    public IndividualBus? IndividualBus { get; set; }

    // The admin's bus assignment this trip was scheduled under. It must still be active to start the trip.
    public Guid? AssignmentId { get; set; }
    public DriverBusAssignment? Assignment { get; set; }

    // The Dhaka calendar day of the departure.
    public DateOnly ScheduleDate { get; set; }

    // Position in the driver's day (1 = first trip), renumbered when the day's trips change.
    public int TripNumber { get; set; }

    [MaxLength(10)]
    public string Direction { get; set; } = DriverTripDirection.Forward;

    // The first and last stops in travel order, copied from the route when the trip is scheduled.
    [MaxLength(100)]
    public string StartStop { get; set; } = "";

    [MaxLength(100)]
    public string EndStop { get; set; } = "";

    // UTC.
    public DateTime ScheduledDeparture { get; set; }
    public DateTime ScheduledArrival { get; set; }
    public DateTime? ActualDeparture { get; set; }
    public DateTime? ActualArrival { get; set; }

    // Whole minutes from the actual departure to the actual arrival, set on completion.
    public int? DurationMinutes { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = DriverTripStatus.Scheduled;

    [MaxLength(500)]
    public string? Notes { get; set; }

    [MaxLength(500)]
    public string? CancelReason { get; set; }

    public Guid? CreatedByAdminId { get; set; }
    public Guid? UpdatedByAdminId { get; set; }

    // Set once the "duty starts soon" reminder was sent, so it is sent only once.
    public DateTime? ReminderSentAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Two requests changing the same trip at once cannot both win.
    public uint Version { get; set; }

    public string Code => $"SCH-{Id.ToString("N")[..8].ToUpperInvariant()}";
}

// The driver's current working status: one row per driver, created the first time it is needed.
public class DriverWorkingStatus
{
    [Key]
    public Guid DriverId { get; set; }
    public User? Driver { get; set; }

    [MaxLength(20)]
    public string CurrentStatus { get; set; } = DriverWorkStatus.OffDuty;

    public DateTime LastStatusChangedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public uint Version { get; set; }
}

// Every working status change, by the driver, by the system (starting or completing a trip) or by an admin.
public class DriverStatusHistory
{
    public Guid Id { get; set; }

    public Guid DriverId { get; set; }
    public User? Driver { get; set; }

    [MaxLength(20)]
    public string? PreviousStatus { get; set; }

    [MaxLength(20)]
    public string NewStatus { get; set; } = "";

    [MaxLength(300)]
    public string? Reason { get; set; }

    // "driver", "system" or "admin".
    [MaxLength(10)]
    public string ChangedBy { get; set; } = "driver";

    public Guid? TripId { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
