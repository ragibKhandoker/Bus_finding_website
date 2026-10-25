using System.ComponentModel.DataAnnotations;

namespace SmartCommute.Server.Models;

// A bus route/service (for example "Turag Paribahan"). Physical vehicles are IndividualBus rows.
public class Bus
{
    public Guid Id { get; set; }

    [MaxLength(100)]
    public string BusName { get; set; } = "";

    [MaxLength(30)]
    public string? RouteNumber { get; set; }

    [MaxLength(100)]
    public string? Operator { get; set; }

    [MaxLength(60)]
    public string? Frequency { get; set; }

    [MaxLength(30)]
    public string BusType { get; set; } = "general";

    public decimal BaseFare { get; set; }
    public decimal PerKmFare { get; set; }

    // Whether the bus also runs the stops in reverse order (the return trip). Dhaka city routes do; set it to
    // false for a one-way service so route search only matches stops in the stored order.
    public bool RunsBothWays { get; set; } = true;

    public List<BusStop> Stops { get; set; } = [];
    public List<IndividualBus> IndividualBuses { get; set; } = [];
}

public class BusStop
{
    public Guid Id { get; set; }
    public Guid BusId { get; set; }
    public Bus? Bus { get; set; }

    // Position of the stop along the route, starting at 0.
    public int Order { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = "";

    public double Lat { get; set; }
    public double Lng { get; set; }
}

public class IndividualBus
{
    public Guid Id { get; set; }
    public Guid BusId { get; set; }
    public Bus? Bus { get; set; }

    [MaxLength(30)]
    public string BusCode { get; set; } = "";

    public int TotalPassengerCapacity { get; set; }
    public int CurrentPassengerCount { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double AverageSpeedKmh { get; set; } = 25;

    [MaxLength(20)]
    public string Status { get; set; } = "active";

    [MaxLength(30)]
    public string BusType { get; set; } = "general";

    // Optimistic concurrency so two boardings cannot both take the last seat.
    public uint Version { get; set; }
}

// Tracks who is currently on which bus, so end-trip only frees a seat the user actually took.
public class BusBoarding
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid IndividualBusId { get; set; }
    public IndividualBus? IndividualBus { get; set; }
    public DateTime BoardedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
}
