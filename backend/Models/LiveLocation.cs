using System.ComponentModel.DataAnnotations;

namespace SmartCommute.Server.Models;

// F23: a driver's live location sharing for one trip. There is one row per trip (unique TripId): it holds only the
// latest fix and is updated in place, so sharing never piles up location history. The row links the existing
// driver (User), route (Bus), vehicle (IndividualBus) and trip (DriverTrip); none of them is duplicated.
// Commuters only ever see rows that are sharing during a trip in progress, and never the driver's identity.
public class LiveLocation
{
    public Guid Id { get; set; }

    public Guid DriverId { get; set; }
    public User? Driver { get; set; }

    public Guid TripId { get; set; }
    public DriverTrip? Trip { get; set; }

    // The route being driven.
    public Guid BusId { get; set; }
    public Bus? Bus { get; set; }

    // The vehicle, when the trip or the assignment names one.
    public Guid? IndividualBusId { get; set; }
    public IndividualBus? IndividualBus { get; set; }

    // The latest fix (null until the first update arrives).
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // Metres, as reported by the device.
    public double? Accuracy { get; set; }

    // km/h (converted from the browser's m/s).
    public double? Speed { get; set; }

    // Degrees from north, 0-360.
    public double? Heading { get; set; }

    public bool IsSharing { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUpdatedAt { get; set; }
    public DateTime? StoppedAt { get; set; }

    // Why sharing ended: "driver", "trip-completed", "trip-ended".
    [MaxLength(20)]
    public string? StopReason { get; set; }

    public int UpdateCount { get; set; }
}
