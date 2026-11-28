using System.ComponentModel.DataAnnotations;

namespace SmartCommute.Server.Models;

// The QR code stuck inside one physical bus (an IndividualBus). Scanning it names the vehicle, and through it the
// route, so the rider can board without picking the bus by hand.
//
// The code holds only a random token (32 random bytes, base64url), never the bus or route id. The server finds the
// code by the token's SHA-256 hash, so the token itself is never searchable in the database; an encrypted copy is
// kept so an admin or the bus's driver can show or print the same code again. A vehicle has at most one active
// code: generating a new one deactivates the old one, so a leaked or photographed code can be retired.
public class BusQrCode
{
    public Guid Id { get; set; }

    public Guid IndividualBusId { get; set; }
    public IndividualBus? IndividualBus { get; set; }

    // Hex SHA-256 of the token; unique.
    [MaxLength(64)]
    public string TokenHash { get; set; } = "";

    // The token, encrypted with SensitiveDataProtector.
    [MaxLength(400)]
    public string TokenCipher { get; set; } = "";

    public bool IsActive { get; set; } = true;

    public Guid? CreatedById { get; set; }
    public User? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? DeactivatedAt { get; set; }
    public Guid? DeactivatedById { get; set; }

    public int ScanCount { get; set; }
    public DateTime? LastScannedAt { get; set; }
}

// One successful scan by a signed-in rider. verify-qr creates it and hands the rider a one-time boarding ticket;
// board-by-qr spends the ticket. A ticket works once, for the rider who scanned, for a few minutes, so a boarding
// request cannot be replayed or sent twice, and a bus or route id from the client is never trusted.
public class BusQrScan
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid QrCodeId { get; set; }
    public BusQrCode? QrCode { get; set; }

    public Guid IndividualBusId { get; set; }

    // Hex SHA-256 of the one-time ticket; unique. The ticket itself is only ever sent to the rider.
    [MaxLength(64)]
    public string TicketHash { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }

    // Set when the ticket was spent on boarding.
    public DateTime? UsedAt { get; set; }
    public Guid? TripId { get; set; }

    // Where the phone was when scanning, if the rider allowed location.
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public static class BoardingMethods
{
    public const string Manual = "manual";
    public const string Qr = "qr";
}
