using System.ComponentModel.DataAnnotations;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.ViewModels;

public class TripViewModel : EntityViewModel
{
    public string UserId { get; init; } = "";
    public string BusId { get; init; } = "";
    public string BusName { get; init; } = "";
    public double Distance { get; init; }
    public decimal Fare { get; init; }
    public string Source { get; init; } = "";
    public string Destination { get; init; } = "";
    public DateTime CreatedAt { get; init; }

    // "active" (on the bus now) or "completed".
    public string Status { get; init; } = "";
    public DateTime StartedAt { get; init; }
    public DateTime? EndedAt { get; init; }
    public int GemsEarned { get; init; }

    // The vehicle boarded and its code (e.g. "DBR-128-01"); null for trips logged by hand.
    public string? IndividualBusId { get; init; }
    public string? BusCode { get; init; }

    public static TripViewModel From(Trip t) => new()
    {
        Id = t.Id.ToString(),
        UserId = t.UserId.ToString(),
        BusId = t.BusId,
        BusName = t.BusName,
        Distance = t.Distance,
        Fare = t.Fare,
        Source = t.Source,
        Destination = t.Destination,
        CreatedAt = t.CreatedAt,
        Status = t.Status,
        StartedAt = t.CreatedAt,
        EndedAt = t.EndedAt,
        GemsEarned = t.GemsEarned,
        IndividualBusId = t.IndividualBusId?.ToString(),
        BusCode = t.IndividualBus?.BusCode,
    };
}

// Boarding a vehicle: where the rider gets on, and optionally where they plan to get off.
// BusId is only read by the older route without the id in the path (POST /individual-bus/board).
public class BoardRequest
{
    public Guid? BusId { get; set; }

    [StringLength(100)]
    public string? BoardingStop { get; set; }

    [StringLength(100)]
    public string? DestinationStop { get; set; }
}

// Ending a trip: the stop where the rider got off (defaults to the destination chosen when boarding).
// Source is only needed for a trip started without a boarding stop.
public class EndTripRequest
{
    public Guid? BusId { get; set; }

    [StringLength(100)]
    public string? Destination { get; set; }

    [StringLength(100)]
    public string? Source { get; set; }
}

public class AddTripRequest
{
    [Required, StringLength(64)]
    public string BusId { get; set; } = "";

    [StringLength(100)]
    public string? BusName { get; set; }

    [Range(0, 1000)]
    public double Distance { get; set; }

    [Range(0, 100_000)]
    public decimal Fare { get; set; }

    [StringLength(200)]
    public string? Source { get; set; }

    [StringLength(200)]
    public string? Destination { get; set; }
}

public class Coordinates
{
    [Range(-90, 90)] public double Lat { get; set; }
    [Range(-180, 180)] public double Lon { get; set; }
}

public class SaveFareRequest
{
    public Guid? UserId { get; set; }

    [Required, StringLength(200)]
    public string Source { get; set; } = "";

    [Required, StringLength(200)]
    public string Destination { get; set; } = "";

    [Range(0, 1000)]
    public double Distance { get; set; }

    public Coordinates? SourceCoordinates { get; set; }
    public Coordinates? DestinationCoordinates { get; set; }
}

public class FareViewModel : EntityViewModel
{
    public string? UserId { get; init; }
    public string Source { get; init; } = "";
    public string Destination { get; init; } = "";
    public double Distance { get; init; }
    public decimal Fare { get; init; }
    public Coordinates? SourceCoordinates { get; init; }
    public Coordinates? DestinationCoordinates { get; init; }
    public DateTime CreatedAt { get; init; }

    public static FareViewModel From(FareRecord f) => new()
    {
        Id = f.Id.ToString(),
        UserId = f.UserId?.ToString(),
        Source = f.Source,
        Destination = f.Destination,
        Distance = f.Distance,
        Fare = f.Fare,
        SourceCoordinates = f.SourceLat is { } sLat && f.SourceLon is { } sLon ? new Coordinates { Lat = sLat, Lon = sLon } : null,
        DestinationCoordinates = f.DestinationLat is { } dLat && f.DestinationLon is { } dLon ? new Coordinates { Lat = dLat, Lon = dLon } : null,
        CreatedAt = f.CreatedAt,
    };
}
