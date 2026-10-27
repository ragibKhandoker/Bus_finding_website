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
    // Offers paid into the wallet use the offer kind as the type: "cashback", "coupon" or "discount".
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

public static class OfferKinds
{
    public const string Cashback = "cashback";
    public const string Coupon = "coupon";
    public const string Discount = "discount";

    public static readonly string[] All = [Cashback, Coupon, Discount];
}

public static class OfferEntryReasons
{
    public const string Welcome = "welcome";     // the configured welcome offer, once per user
    public const string Opening = "opening";     // what an account already had when the ledger started
    public const string Granted = "granted";     // added by an admin
    public const string Adjusted = "adjusted";   // set by an admin
    public const string Redeemed = "redeemed";   // cashback paid into the wallet
    public const string Trip = "trip";           // coupon or discount taken off a trip's fare

    public static readonly string[] All = [Welcome, Opening, Granted, Adjusted, Redeemed, Trip];
}

// A user's offer balances, in taka: cashback (paid into the wallet when redeemed), coupon and discount value
// (taken off a completed trip's fare). Every change is recorded as an OfferTransaction.
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

// In-app stored-value "GPay" account. Not connected to any real payment provider: it starts with
// Rewards:GpayStartingBalance, can pay trip fares and move money into the commute wallet, and only an admin can
// add money (a test top-up). Every change is recorded as a GpayTransaction.
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

// The audit trail of offer balances: + for value given, - for value used. Unique indexes stop the same welcome
// offer, the same client reference, or a second coupon (or discount) on one trip from being recorded twice.
public class OfferTransaction
{
    public Guid Id { get; set; }
    public Guid OfferId { get; set; }
    public Offer? Offer { get; set; }
    public Guid UserId { get; set; }

    [MaxLength(20)]
    public string Kind { get; set; } = OfferKinds.Cashback;

    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }

    [MaxLength(20)]
    public string Reason { get; set; } = OfferEntryReasons.Granted;

    [MaxLength(200)]
    public string Description { get; set; } = "";

    // The trip a coupon or discount was used on.
    public Guid? TripId { get; set; }
    public Trip? Trip { get; set; }

    // The wallet credit this produced, if any.
    public Guid? WalletTransactionId { get; set; }
    public WalletTransaction? WalletTransaction { get; set; }

    // A key from the client or admin that makes a repeated request a no-op; welcome entries use "welcome:<kind>".
    [MaxLength(100)]
    public string? Reference { get; set; }

    // The admin who granted or adjusted it.
    public Guid? CreatedById { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class GpayTransactionTypes
{
    public const string Opening = "opening";               // the starting balance (or the balance when the ledger began)
    public const string WalletRecharge = "wallet-recharge"; // moved into the commute wallet
    public const string TripPayment = "trip-payment";       // paid a completed trip's fare
    public const string Payment = "payment";               // any other payment (the older /gpay/deduct)
    public const string TopUp = "top-up";                  // added by an admin (testing; there is no payment provider)

    public static readonly string[] All = [Opening, WalletRecharge, TripPayment, Payment, TopUp];
}

// The GPay ledger: + money in, - money out. A unique index allows one payment per trip, another makes a repeated
// client reference a no-op.
public class GpayTransaction
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public GpayAccount? Account { get; set; }
    public Guid UserId { get; set; }

    [MaxLength(20)]
    public string Type { get; set; } = GpayTransactionTypes.Payment;

    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }

    [MaxLength(200)]
    public string Description { get; set; } = "";

    public Guid? TripId { get; set; }
    public Trip? Trip { get; set; }

    public Guid? WalletTransactionId { get; set; }
    public WalletTransaction? WalletTransaction { get; set; }

    [MaxLength(100)]
    public string? Reference { get; set; }

    // The admin who made a top-up.
    public Guid? CreatedById { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
