using System.ComponentModel.DataAnnotations;

namespace SmartCommute.Server.Models;

public class Wallet
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public decimal Balance { get; set; }
    public int Gems { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; } = "BDT";

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public uint Version { get; set; }
}

// The Type values written to the wallet ledger.
public static class WalletTransactionTypes
{
    public const string Fare = "fare";                     // taka paid from the wallet
    public const string Recharge = "recharge";             // taka added from GPay
    public const string GemsEarned = "gems-earned";        // gems rewarded for a trip or ride share
    public const string GemsConverted = "gems-converted";  // gems turned into taka
    // Offers paid into the wallet use the offer kind as the type: "cashback" or "coupon".
}

// Audit trail of every balance or gem change.
public class WalletTransaction
{
    public Guid Id { get; set; }
    public Guid WalletId { get; set; }
    public Wallet? Wallet { get; set; }

    [MaxLength(30)]
    public string Type { get; set; } = "";

    // Taka added (positive) or taken (negative), and gems added or taken.
    public decimal Amount { get; set; }
    public int GemsChange { get; set; }

    // The wallet's balances right after this change; null for entries recorded before they were stored.
    public decimal? BalanceAfter { get; set; }
    public int? GemsAfter { get; set; }

    [MaxLength(200)]
    public string Description { get; set; } = "";

    // The trip a gems reward was for. Unique, so a trip can never be rewarded twice.
    public Guid? TripId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Offer
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid WalletId { get; set; }
    public Wallet? Wallet { get; set; }

    public decimal Cashback { get; set; }
    public decimal Coupon { get; set; }
    public decimal Discount { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// In-app stored-value "GPay" account. Not connected to any real payment provider.
public class GpayAccount
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string PinHash { get; set; } = "";
    public decimal Balance { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public uint Version { get; set; }
}
