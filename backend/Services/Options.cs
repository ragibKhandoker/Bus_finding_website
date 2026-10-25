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
