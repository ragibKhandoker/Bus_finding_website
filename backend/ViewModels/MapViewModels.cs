namespace SmartCommute.Server.ViewModels;

// A bus stop's name and position.
public record StopPointViewModel(string Name, double Lat, double Lng);

// A bus stop near a point, with the straight-line distance to it.
public record NearbyStopViewModel(string Name, double Lat, double Lng, double DistanceKm);

// Driving distance and time between two stops. Source: "google-routes", "openstreetmap", or "approximate" when
// the router was unreachable and the distance is a straight-line estimate.
public record RouteEstimateViewModel(
    double DistanceKm, double DurationMinutes, double? DurationWithoutTrafficMinutes, string? Polyline,
    BusStopViewModel Start, BusStopViewModel End, string Source);
