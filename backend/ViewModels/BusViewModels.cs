using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.ViewModels;

public class BusStopViewModel
{
    public string Name { get; init; } = "";
    public double Lat { get; init; }
    public double Lng { get; init; }

    public static BusStopViewModel From(BusStop s) => new() { Name = s.Name, Lat = s.Lat, Lng = s.Lng };
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

    public List<BusStopViewModel> Stops { get; init; } = [];

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
        Stops = b.Stops.OrderBy(s => s.Order).Select(BusStopViewModel.From).ToList(),
    };
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
