using System.ComponentModel.DataAnnotations;

namespace SmartCommute.Server.Models;

public static class TripStatus
{
    // On the bus now: started by boarding, waiting for the rider to end it.
    public const string Active = "active";

    // Finished: ended at a stop (fare and gems settled) or logged straight into history.
    public const string Completed = "completed";
}

// One journey. CreatedAt is when it started (boarding, or when it was logged).
public class Trip
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    // The vehicle boarded, for trips started with board; null for trips logged into history by hand.
    public Guid? IndividualBusId { get; set; }
    public IndividualBus? IndividualBus { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = TripStatus.Completed;

    public DateTime? EndedAt { get; set; }

    // Gems credited when the trip was completed; 0 for trips logged by hand.
    public int GemsEarned { get; set; }

    // Optimistic concurrency: two simultaneous end-trip requests cannot both complete the trip.
    public uint Version { get; set; }

    // Kept as free text: clients may log a trip for a bus id typed in by the user.
    [MaxLength(64)]
    public string BusId { get; set; } = "";

    [MaxLength(100)]
    public string BusName { get; set; } = "";

    public double Distance { get; set; }
    public decimal Fare { get; set; }

    [MaxLength(200)]
    public string Source { get; set; } = "";

    [MaxLength(200)]
    public string Destination { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ---- A snapshot of the ride, taken when boarding and completed when the trip ends, so the rider's history
    // keeps its full details even if the route or the vehicle is later edited or removed. Null on trips logged by
    // hand and on trips from before these were saved.

    [MaxLength(40)]
    public string? RouteNumber { get; set; }

    [MaxLength(40)]
    public string? BusType { get; set; }

    [MaxLength(100)]
    public string? Operator { get; set; }

    // The vehicle's code (e.g. "R-11-03"), kept here as well as through IndividualBus.
    [MaxLength(40)]
    public string? BusCode { get; set; }

    // The rates the fare was worked out with.
    public decimal? BaseFare { get; set; }
    public decimal? PerKmFare { get; set; }

    // Every stop from boarding to getting off, in the order ridden.
    public List<string>? StopsRidden { get; set; }
}

public class FareRecord
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(200)]
    public string Source { get; set; } = "";

    [MaxLength(200)]
    public string Destination { get; set; } = "";

    public double Distance { get; set; }
    public decimal Fare { get; set; }

    // The Rewards:FarePerKm rate the fare was worked out with; null for records saved before it was stored.
    public decimal? FarePerKm { get; set; }

    // True when the road router was unreachable and Distance is a straight-line estimate.
    public bool DistanceEstimated { get; set; }

    public double? SourceLat { get; set; }
    public double? SourceLon { get; set; }
    public double? DestinationLat { get; set; }
    public double? DestinationLon { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
