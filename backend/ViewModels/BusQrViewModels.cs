using System.ComponentModel.DataAnnotations;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.ViewModels;

// A vehicle's QR code, for an admin or the bus's driver. Token is what goes in the code; the app turns it into a
// link (<site>/scan?c=<token>) so a phone's own camera app opens SmartCommute too.
public class BusQrCodeViewModel
{
    public string? Id { get; init; }
    public string VehicleId { get; init; } = "";
    public string RouteId { get; init; } = "";
    public string BusCode { get; init; } = "";
    public string? BusName { get; init; }
    public string? RouteNumber { get; init; }

    // False when the vehicle has no active code yet.
    public bool HasCode { get; init; }
    public string? Token { get; init; }
    public bool IsActive { get; init; }
    public DateTime? CreatedAt { get; init; }
    public int ScanCount { get; init; }
    public DateTime? LastScannedAt { get; init; }

    public static BusQrCodeViewModel Without(IndividualBus v) => new()
    {
        VehicleId = v.Id.ToString(),
        RouteId = v.BusId.ToString(),
        BusCode = v.BusCode,
        BusName = v.Bus?.BusName,
        RouteNumber = v.Bus?.RouteNumber,
    };
}

public class QrVehicleViewModel
{
    public string Id { get; init; } = "";
    public string BusCode { get; init; } = "";
    public string BusType { get; init; } = "";
    public int TotalPassengerCapacity { get; init; }
    public int CurrentPassengerCount { get; init; }
    public int SeatsFree { get; init; }
}

// What a successful scan shows the rider: the bus, its route and stops, and the one-time ticket to board with.
public class QrScanViewModel
{
    public string ScanTicket { get; init; } = "";
    public DateTime ExpiresAt { get; init; }
    public QrVehicleViewModel Vehicle { get; init; } = new();
    public BusViewModel Route { get; init; } = new();

    // The route's stop nearest to the phone, when location was shared and a stop is within 1 km.
    public string? NearestStop { get; init; }
    public double? NearestStopKm { get; init; }

    // The rider's trip in progress, if any (they must end it before boarding another bus).
    public TripViewModel? ActiveTrip { get; init; }
    public bool OnThisBus { get; init; }
}

public class VerifyQrRequest
{
    // What the camera read: the token, or the link with ?c=<token>.
    [Required(ErrorMessage = "Scan a QR code first."), StringLength(500)]
    public string Code { get; set; } = "";

    [Range(-90, 90)] public double? Lat { get; set; }
    [Range(-180, 180)] public double? Lng { get; set; }
}

public class BoardByQrRequest
{
    [Required(ErrorMessage = "Scan the bus QR code first."), StringLength(100)]
    public string? ScanTicket { get; set; }

    [StringLength(100)]
    public string? BoardingStop { get; set; }

    [StringLength(100)]
    public string? DestinationStop { get; set; }

    [Range(-90, 90)] public double? Lat { get; set; }
    [Range(-180, 180)] public double? Lng { get; set; }
}

public class EndActiveTripRequest
{
    // The stop where the rider got off; leave out to use the stop nearest to Lat/Lng.
    [StringLength(100)]
    public string? Destination { get; set; }

    [Range(-90, 90)] public double? Lat { get; set; }
    [Range(-180, 180)] public double? Lng { get; set; }
}
