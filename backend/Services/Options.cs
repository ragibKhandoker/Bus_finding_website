namespace SmartCommute.Server.Services;

public class JwtOptions
{
    public string Secret { get; set; } = "";
    public string Issuer { get; set; } = "SmartCommute";
    public string Audience { get; set; } = "SmartCommuteClients";
    public int ExpiryDays { get; set; } = 7;
}

public class RewardOptions
{
    // Used for distance-based fare estimates.
    public decimal FarePerKm { get; set; } = 30;
    public int GemsPerTrip { get; set; } = 10;
    public int GemsPerTaka { get; set; } = 50;
    public decimal WelcomeCashback { get; set; }
    public decimal WelcomeCoupon { get; set; }
    public decimal WelcomeDiscount { get; set; }
    public decimal GpayStartingBalance { get; set; } = 5000;
}

public class RideShareOptions
{
    // Most people one ride post can be for, counting the host. The database refuses anything above 20.
    public int MaxParticipants { get; set; } = 10;
}

public class VerificationOptions
{
    // Largest verification image accepted, in bytes.
    public int MaxImageBytes { get; set; } = 5 * 1024 * 1024;

    // Where uploaded documents are kept, relative to the app folder. It must not be inside wwwroot.
    public string StoragePath { get; set; } = "App_Data/verification-images";
}

public class BusPhotoOptions
{
    // Largest bus photo accepted, in bytes.
    public int MaxImageBytes { get; set; } = 5 * 1024 * 1024;

    // Where bus photos are kept, relative to the app folder. They are public, but served through BusController so
    // only checked files with random names can ever be fetched.
    public string StoragePath { get; set; } = "App_Data/bus-photos";
}

public class GeoOptions
{
    public string NominatimBaseUrl { get; set; } = "https://nominatim.openstreetmap.org";
    public string OsrmBaseUrl { get; set; } = "https://router.project-osrm.org";
    public string UserAgent { get; set; } = "SmartCommuteDhaka/1.0";
}

// Google Maps settings for the server. ServerApiKey is secret: set it in appsettings.Local.json or the
// GoogleMaps__ServerApiKey environment variable, never in the frontend.
public class GoogleMapsOptions
{
    public string ServerApiKey { get; set; } = "";
    public string RoutesBaseUrl { get; set; } = "https://routes.googleapis.com";
}

// F15 defaults. They seed the reward_settings row and the membership tiers the first time the server starts; after
// that the admin changes them on the Admin page and these values are not read again.
public class PromotionOptions
{
    public bool CashbackEnabled { get; set; } = true;
    public decimal CashbackPercent { get; set; } = 5;
    public decimal CashbackFixed { get; set; }
    public decimal CashbackMinFare { get; set; } = 20;
    public decimal CashbackMaxPerTrip { get; set; } = 10;
    public decimal CashbackMaxPerUserMonthly { get; set; } = 300;
    public int CashbackExpiryDays { get; set; } = 90;
    public bool AllowCouponStacking { get; set; }
    public bool AllowCouponWithCashback { get; set; } = true;
    public int NewUserDays { get; set; } = 30;
    public decimal FirstTripCashback { get; set; } = 10;
    public bool ReferralEnabled { get; set; } = true;
    public decimal ReferrerReward { get; set; } = 25;
    public decimal ReferredReward { get; set; } = 15;
    public int ReferralApplyDays { get; set; } = 7;
    public int MaxReferralRewardsPerMonth { get; set; } = 20;

    public List<TierOption> Tiers { get; set; } = [];

    // Adds example coupons and a campaign when the server runs in Development (never in production). Safe to run
    // on every start: nothing that already exists is changed.
    public bool SeedDemoOffers { get; set; } = true;

    // The background job that expires campaigns and cashback, reminds users of expiring coupons, announces new
    // campaigns and processes any completed trip whose rewards were missed.
    public bool LifecycleEnabled { get; set; } = true;
    public int LifecycleIntervalSeconds { get; set; } = 60;
}

public class TierOption
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int MinTrips { get; set; }
    public decimal MinSpend { get; set; }
    public decimal ExtraCashbackPercent { get; set; }
    public decimal ExtraCapPerTrip { get; set; }
    public decimal ExtraMonthlyCap { get; set; }
    public string Benefits { get; set; } = "";
}

// F17: pictures of lost and found items.
public class LostFoundOptions
{
    public int MaxImageBytes { get; set; } = 5 * 1024 * 1024;

    // Relative to the app folder, outside wwwroot; files are served only through LostFoundController.
    public string StoragePath { get; set; } = "App_Data/lost-found-images";
}
