using System.ComponentModel.DataAnnotations;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.ViewModels;

public class WalletViewModel : EntityViewModel
{
    public string UserId { get; init; } = "";
    public decimal Balance { get; init; }
    public int Gems { get; init; }
    public string Currency { get; init; } = "";
    public DateTime LastUpdated { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    public static WalletViewModel From(Wallet w) => new()
    {
        Id = w.Id.ToString(),
        UserId = w.UserId.ToString(),
        Balance = w.Balance,
        Gems = w.Gems,
        Currency = w.Currency,
        LastUpdated = w.LastUpdated,
        CreatedAt = w.CreatedAt,
        UpdatedAt = w.UpdatedAt,
    };
}

public class OfferViewModel : EntityViewModel
{
    public string UserId { get; init; } = "";
    // Flutter's Offers model reads the wallet object from "walletId".
    public WalletViewModel WalletId { get; init; } = new();
    public decimal Cashback { get; init; }
    public decimal Coupon { get; init; }
    public decimal Discount { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    public static OfferViewModel From(Offer o) => new()
    {
        Id = o.Id.ToString(),
        UserId = o.UserId.ToString(),
        WalletId = WalletViewModel.From(o.Wallet!),
        Cashback = o.Cashback,
        Coupon = o.Coupon,
        Discount = o.Discount,
        IsActive = o.IsActive,
        CreatedAt = o.CreatedAt,
        UpdatedAt = o.UpdatedAt,
    };
}

public class UpdateOffersRequest
{
    [Range(0, 1_000_000)] public decimal Cashback { get; set; }
    [Range(0, 1_000_000)] public decimal Coupon { get; set; }
    [Range(0, 1_000_000)] public decimal Discount { get; set; }
    public bool IsActive { get; set; } = true;
    // Admins may target another user; otherwise the caller's own offers are updated.
    public Guid? UserId { get; set; }
}

public class AdminAmountRequest : AmountRequest
{
    public Guid? UserId { get; set; }
}

public class DeductRequest : AmountRequest
{
    [StringLength(200)]
    public string? Description { get; set; }
}

public class GpayViewModel : EntityViewModel
{
    public string UserId { get; init; } = "";
    public decimal Balance { get; init; }
    // Only filled in when the user has just created the account or typed the PIN.
    public string? DisplayCode { get; init; }
    public DateTime CreatedAt { get; init; }

    public static GpayViewModel From(GpayAccount a, string? displayCode = null) => new()
    {
        Id = a.Id.ToString(),
        UserId = a.UserId.ToString(),
        Balance = a.Balance,
        DisplayCode = displayCode,
        CreatedAt = a.CreatedAt,
    };
}

public class GpayLoginRequest
{
    [Required, RegularExpression(@"^\d{4}$", ErrorMessage = "PIN must be 4 digits.")]
    public string Code { get; set; } = "";
}
