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

// Audit trail of every balance or gem change.
public class WalletTransaction
{
    public Guid Id { get; set; }
    public Guid WalletId { get; set; }
    public Wallet? Wallet { get; set; }

    [MaxLength(30)]
    public string Type { get; set; } = "";

    public decimal Amount { get; set; }
    public int GemsChange { get; set; }

    [MaxLength(200)]
    public string Description { get; set; } = "";

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
