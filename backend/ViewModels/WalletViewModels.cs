using System.ComponentModel.DataAnnotations;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

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

// One ledger entry. Amount is taka (+ added, - taken); GemsChange is gems (+ earned, - converted).
public class WalletTransactionViewModel : EntityViewModel
{
    public string Type { get; init; } = "";
    public decimal Amount { get; init; }
    public int GemsChange { get; init; }
    public decimal? BalanceAfter { get; init; }
    public int? GemsAfter { get; init; }
    public string Description { get; init; } = "";
    public DateTime CreatedAt { get; init; }

    public static WalletTransactionViewModel From(WalletTransaction t) => new()
    {
        Id = t.Id.ToString(),
        Type = t.Type,
        Amount = t.Amount,
        GemsChange = t.GemsChange,
        BalanceAfter = t.BalanceAfter,
        GemsAfter = t.GemsAfter,
        Description = t.Description,
        CreatedAt = t.CreatedAt,
    };
}

// gems: how many gems to convert; leave it out to convert as many as possible.
public class ConvertGemsRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Enter a number of gems greater than zero.")]
    public int? Gems { get; set; }
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

// The details the Offers page shows besides the balances: what the welcome offer was, recent offer history, and
// completed trips a coupon or discount can still be used on.
public class OfferDetailsViewModel : OfferViewModel
{
    public WelcomeOfferViewModel Welcome { get; init; } = new();
    public List<OfferTransactionViewModel> History { get; init; } = [];
    public List<TripOfferViewModel> Trips { get; init; } = [];

    public static OfferDetailsViewModel From(Offer o, RewardOptions rewards, List<OfferTransaction> history, List<TripOfferView> trips)
    {
        var basic = OfferViewModel.From(o);
        return new()
        {
            Id = basic.Id, UserId = basic.UserId, WalletId = basic.WalletId,
            Cashback = basic.Cashback, Coupon = basic.Coupon, Discount = basic.Discount, IsActive = basic.IsActive,
            CreatedAt = basic.CreatedAt, UpdatedAt = basic.UpdatedAt,
            Welcome = new WelcomeOfferViewModel { Cashback = rewards.WelcomeCashback, Coupon = rewards.WelcomeCoupon, Discount = rewards.WelcomeDiscount },
            History = history.Select(OfferTransactionViewModel.From).ToList(),
            Trips = trips.Select(TripOfferViewModel.From).ToList(),
        };
    }
}

public class WelcomeOfferViewModel
{
    public decimal Cashback { get; init; }
    public decimal Coupon { get; init; }
    public decimal Discount { get; init; }
}

public class OfferTransactionViewModel : EntityViewModel
{
    public string Kind { get; init; } = "";
    // + value given, - value used.
    public decimal Amount { get; init; }
    public decimal BalanceAfter { get; init; }
    public string Reason { get; init; } = "";
    public string Description { get; init; } = "";
    public string? TripId { get; init; }
    public DateTime CreatedAt { get; init; }

    public static OfferTransactionViewModel From(OfferTransaction x) => new()
    {
        Id = x.Id.ToString(),
        Kind = x.Kind,
        Amount = x.Amount,
        BalanceAfter = x.BalanceAfter,
        Reason = x.Reason,
        Description = x.Description,
        TripId = x.TripId?.ToString(),
        CreatedAt = x.CreatedAt,
    };
}

public class TripOfferViewModel
{
    public string TripId { get; init; } = "";
    public string BusName { get; init; } = "";
    public string Source { get; init; } = "";
    public string Destination { get; init; } = "";
    public DateTime? EndedAt { get; init; }
    public decimal Fare { get; init; }
    public decimal CouponSaved { get; init; }
    public decimal DiscountSaved { get; init; }
    public decimal RemainingFare { get; init; }
    public bool CouponUsed => CouponSaved > 0;
    public bool DiscountUsed => DiscountSaved > 0;

    public static TripOfferViewModel From(TripOfferView t) => new()
    {
        TripId = t.TripId.ToString(), BusName = t.BusName, Source = t.Source, Destination = t.Destination, EndedAt = t.EndedAt,
        Fare = t.Fare, CouponSaved = t.CouponSaved, DiscountSaved = t.DiscountSaved, RemainingFare = t.RemainingFare,
    };
}

// Admin only. Leaving userId out targets the admin's own offers.
public class UpdateOffersRequest
{
    [Range(0, 1_000_000)] public decimal Cashback { get; set; }
    [Range(0, 1_000_000)] public decimal Coupon { get; set; }
    [Range(0, 1_000_000)] public decimal Discount { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? UserId { get; set; }
}

// Admin only. reference (optional) makes a repeated grant a no-op.
public class AdminAmountRequest : AmountRequest
{
    public Guid? UserId { get; set; }

    [StringLength(100)]
    public string? Reference { get; set; }
}

// amount left out redeems all the cashback. reference (optional, e.g. a random id per button press) makes a
// repeated request return the first result instead of paying out again.
public class UseCashbackRequest
{
    [Range(0.01, 1_000_000, ErrorMessage = "Amount must be greater than zero.")]
    public decimal? Amount { get; set; }

    [StringLength(100)]
    public string? Reference { get; set; }
}

// Uses coupon or discount value on one of the caller's completed trips. amount left out uses as much as possible.
public class UseOnTripRequest
{
    [Required(ErrorMessage = "Choose a completed trip to use it on.")]
    public Guid? TripId { get; set; }

    [Range(0.01, 1_000_000, ErrorMessage = "Amount must be greater than zero.")]
    public decimal? Amount { get; set; }

    [StringLength(100)]
    public string? Reference { get; set; }
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
