using System.ComponentModel.DataAnnotations;

namespace SmartCommute.Server.Models;

public class Trip
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

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

    public double? SourceLat { get; set; }
    public double? SourceLon { get; set; }
    public double? DestinationLat { get; set; }
    public double? DestinationLon { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
