using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Server.ViewModels;

// Body of POST /api/live-location/update. speed in km/h, heading in degrees, accuracy in metres.
public class LiveLocationUpdateRequest
{
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? Accuracy { get; set; }
    public double? Speed { get; set; }
    public double? Heading { get; set; }
}

// A bus on the live map. Coordinates only while it is sharing; never anything about the driver.
public class LiveBusViewModel
{
    public string SessionId { get; init; } = "";
    public string BusId { get; init; } = "";
    public string? VehicleId { get; init; }
    public string BusName { get; init; } = "";
    public string? RouteNumber { get; init; }
    public string? Operator { get; init; }
    public string BusType { get; init; } = "";
    public string? RegistrationNumber { get; init; }
    public string Direction { get; init; } = "";
    public string StartStop { get; init; } = "";
    public string EndStop { get; init; } = "";
    public bool IsSharing { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? Accuracy { get; init; }
    public double? Speed { get; init; }
    public double? Heading { get; init; }
    public DateTime? LastUpdatedAt { get; init; }
    public int? SecondsAgo { get; init; }
    // "live", "delayed" or "unavailable" (see LiveLocation:LiveSeconds / DelayedSeconds).
    public string Freshness { get; init; } = "unavailable";
    public DateTime? StartedAt { get; init; }

    public static LiveBusViewModel From(LiveBus b, DateTime now, LiveLocationOptions o) => new()
    {
        SessionId = b.SessionId.ToString(), BusId = b.BusId.ToString(), VehicleId = b.VehicleId?.ToString(), BusName = b.BusName, RouteNumber = b.RouteNumber,
        Operator = b.Operator, BusType = b.BusType, RegistrationNumber = b.RegistrationNumber, Direction = b.Direction, StartStop = b.From, EndStop = b.To,
        IsSharing = b.IsSharing, Latitude = b.Latitude, Longitude = b.Longitude, Accuracy = b.Accuracy, Speed = b.Speed, Heading = b.Heading,
        LastUpdatedAt = b.LastUpdatedAt, SecondsAgo = b.LastUpdatedAt is { } last ? (int)Math.Max(0, (now - last).TotalSeconds) : null,
        Freshness = RouteProgress.Freshness(b.LastUpdatedAt, now, o), StartedAt = b.StartedAt,
    };

    // A route or vehicle with nobody sharing right now.
    public static LiveBusViewModel NotSharing(Guid busId, string busName, string? routeNumber, string? op, string busType, string? registration) => new()
    {
        BusId = busId.ToString(), BusName = busName, RouteNumber = routeNumber, Operator = op, BusType = busType, RegistrationNumber = registration,
        IsSharing = false, Freshness = "unavailable",
    };
}

public record ProgressStopViewModel(int Order, string Name, double Lat, double Lng, double KmFromStart)
{
    public static ProgressStopViewModel? From(ProgressStop? s) => s is null ? null : new(s.Order, s.Name, s.Lat, s.Lng, s.KmFromStart);
}

// The driver's own sharing state, for the dashboard.
public class DriverLiveStateViewModel
{
    public bool CanShare { get; init; }
    public string? CannotShareReason { get; init; }
    public object? Trip { get; init; }
    public object? Bus { get; init; }
    public bool IsSharing { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? Accuracy { get; init; }
    public DateTime? LastUpdatedAt { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? StoppedAt { get; init; }
    public string? StopReason { get; init; }
    public string Freshness { get; init; } = "unavailable";
    public int UpdateCount { get; init; }
    public object Settings { get; init; } = new { };

    public static DriverLiveStateViewModel From(DriverLiveState s, DateTime now, LiveLocationOptions o)
    {
        var trip = s.Trip;
        var bus = trip?.Bus ?? s.Assignment?.Bus;
        var vehicle = trip?.IndividualBus ?? s.Assignment?.IndividualBus;
        return new()
        {
            CanShare = s.CannotStartReason is null,
            CannotShareReason = s.CannotStartReason,
            Trip = trip is null ? null : new
            {
                id = trip.Id, code = trip.Code, tripNumber = trip.TripNumber, status = trip.Status, from = trip.StartStop, to = trip.EndStop,
                direction = trip.Direction, scheduledDeparture = trip.ScheduledDeparture, scheduledArrival = trip.ScheduledArrival, actualDeparture = trip.ActualDeparture,
            },
            Bus = bus is null ? null : new { id = bus.Id, busName = bus.BusName, routeNumber = bus.RouteNumber, @operator = bus.Operator, registrationNumber = vehicle?.BusCode, vehicleId = vehicle?.Id },
            IsSharing = s.Sharing?.IsSharing == true,
            Latitude = s.Sharing?.Latitude,
            Longitude = s.Sharing?.Longitude,
            Accuracy = s.Sharing?.Accuracy,
            LastUpdatedAt = s.Sharing?.LastUpdatedAt,
            StartedAt = s.Sharing?.StartedAt,
            StoppedAt = s.Sharing?.StoppedAt,
            StopReason = s.Sharing?.StopReason,
            Freshness = RouteProgress.Freshness(s.Sharing?.LastUpdatedAt, now, o),
            UpdateCount = s.Sharing?.UpdateCount ?? 0,
            Settings = new { liveSeconds = o.LiveSeconds, delayedSeconds = o.DelayedSeconds, minUpdateIntervalSeconds = o.MinUpdateIntervalSeconds },
        };
    }
}
