using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.ViewModels;

public class BusStopViewModel
{
    public string Name { get; init; } = "";
    public double Lat { get; init; }
    public double Lng { get; init; }

    // Position along the route, starting at 0.
    public int Order { get; init; }

    public static BusStopViewModel From(BusStop s) => new() { Name = s.Name, Lat = s.Lat, Lng = s.Lng, Order = s.Order };
}

public class BusViewModel : EntityViewModel
{
    public string BusName { get; init; } = "";
    public string? RouteNumber { get; init; }
    public string? Operator { get; init; }
    public string? Frequency { get; init; }
    public string BusType { get; init; } = "";

    [JsonPropertyName("base_fare")]
    public decimal BaseFare { get; init; }

    [JsonPropertyName("per_km_fare")]
    public decimal PerKmFare { get; init; }

    public bool RunsBothWays { get; init; }

    public List<BusStopViewModel> Stops { get; init; } = [];

    // Set only on route search results: how this bus serves the searched trip.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RouteMatchViewModel? Match { get; init; }

    // A copy carrying a route search match; cached routes are shared, so they are never changed in place.
    public BusViewModel WithMatch(RouteMatchViewModel match) => new()
    {
        Id = Id, BusName = BusName, RouteNumber = RouteNumber, Operator = Operator, Frequency = Frequency,
        BusType = BusType, BaseFare = BaseFare, PerKmFare = PerKmFare, RunsBothWays = RunsBothWays, Stops = Stops,
        Match = match,
    };

    public static BusViewModel From(Bus b) => new()
    {
        Id = b.Id.ToString(),
        BusName = b.BusName,
        RouteNumber = b.RouteNumber,
        Operator = b.Operator,
        Frequency = b.Frequency,
        BusType = b.BusType,
        BaseFare = b.BaseFare,
        PerKmFare = b.PerKmFare,
        RunsBothWays = b.RunsBothWays,
        Stops = b.Stops.OrderBy(s => s.Order).Select(BusStopViewModel.From).ToList(),
    };
}

// How a bus serves a searched trip: where to board and get off, and the stops ridden in travel order.
public class RouteMatchViewModel
{
    // "forward" follows the stored stop order; "return" is the same route run in reverse.
    public string Direction { get; init; } = "forward";
    public BusStopViewModel Boarding { get; init; } = new();
    public BusStopViewModel Alighting { get; init; } = new();

    // Stops from boarding to getting off, both included, in the order the bus reaches them.
    public List<BusStopViewModel> Segment { get; init; } = [];

    // Stops after boarding up to and including the one where the rider gets off.
    public int StopsToRide => Segment.Count - 1;
}

public class IndividualBusViewModel : EntityViewModel
{
    public string ParentBusInfoId { get; init; } = "";
    public string BusCode { get; init; } = "";
    public int TotalPassengerCapacity { get; init; }
    public int CurrentPassengerCount { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double AverageSpeedKmh { get; init; }
    public string Status { get; init; } = "";
    public string BusType { get; init; } = "";

    public static IndividualBusViewModel From(IndividualBus b) => new()
    {
        Id = b.Id.ToString(),
        ParentBusInfoId = b.BusId.ToString(),
        BusCode = b.BusCode,
        TotalPassengerCapacity = b.TotalPassengerCapacity,
        CurrentPassengerCount = b.CurrentPassengerCount,
        Latitude = b.Latitude,
        Longitude = b.Longitude,
        AverageSpeedKmh = b.AverageSpeedKmh,
        Status = b.Status,
        BusType = b.BusType,
    };
}

public class BusIdRequest
{
    [Required]
    public Guid? BusId { get; set; }
}
