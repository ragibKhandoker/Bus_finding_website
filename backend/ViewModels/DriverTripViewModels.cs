using System.ComponentModel.DataAnnotations;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Server.ViewModels;

// ---- F22 requests ----

// An admin scheduling (or changing) one trip. Date is yyyy-MM-dd and times HH:mm, both Dhaka time.
public class DriverScheduleRequest
{
    public Guid? DriverId { get; set; }
    public Guid? BusId { get; set; }
    public Guid? IndividualBusId { get; set; }
    [StringLength(10)] public string? Date { get; set; }
    [StringLength(8)] public string? DepartureTime { get; set; }
    [StringLength(8)] public string? ArrivalTime { get; set; }
    [StringLength(10)] public string? Direction { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
}

public class WorkingStatusRequest
{
    [Required(ErrorMessage = "Choose a status.")]
    [StringLength(20)]
    public string? Status { get; set; }

    [StringLength(300)]
    public string? Reason { get; set; }
}

public class ReasonRequest
{
    [StringLength(500)]
    public string? Reason { get; set; }
}

public class AdminAssignmentRequest
{
    public Guid? DriverId { get; set; }
    public Guid? BusId { get; set; }
    public Guid? IndividualBusId { get; set; }
}

// ---- F22 responses (times are UTC; clients show them in Asia/Dhaka) ----

public record DriverVehicleViewModel(string Id, string Code, string BusType, int Capacity, string Status)
{
    public static DriverVehicleViewModel? From(IndividualBus? v) => v is null ? null : new(v.Id.ToString(), v.BusCode, v.BusType, v.TotalPassengerCapacity, v.Status);
}

public record DriverRouteStopViewModel(int Order, string Name, double Lat, double Lng);

public record DriverRouteViewModel(string Id, string BusName, string? RouteNumber, string? Operator, string BusType, string StartStop, string EndStop,
    int TotalStops, double DistanceKm, bool RunsBothWays, decimal BaseFare, decimal PerKmFare, string? Frequency, List<DriverRouteStopViewModel> Stops)
{
    public static DriverRouteViewModel From(Bus b)
    {
        var stops = b.Stops.OrderBy(s => s.Order).ToList();
        var km = 0.0;
        for (var i = 1; i < stops.Count; i++)
        {
            km += GeoService.HaversineKm(stops[i - 1].Lat, stops[i - 1].Lng, stops[i].Lat, stops[i].Lng);
        }
        return new(b.Id.ToString(), b.BusName, b.RouteNumber, b.Operator, b.BusType, stops.FirstOrDefault()?.Name ?? "", stops.LastOrDefault()?.Name ?? "",
            stops.Count, Math.Round(km, 1), b.RunsBothWays, b.BaseFare, b.PerKmFare, b.Frequency,
            stops.Select(s => new DriverRouteStopViewModel(s.Order, s.Name, s.Lat, s.Lng)).ToList());
    }
}

public class DriverTripViewModel
{
    public string Id { get; init; } = "";
    public string Code { get; init; } = "";
    public int TripNumber { get; init; }
    public string ScheduleDate { get; init; } = "";
    public string Direction { get; init; } = "";
    public string Status { get; init; } = "";
    public string RouteId { get; init; } = "";
    public string BusName { get; init; } = "";
    public string? RouteNumber { get; init; }
    public string? Operator { get; init; }
    public string BusType { get; init; } = "";
    public DriverVehicleViewModel? Vehicle { get; init; }
    public string StartStop { get; init; } = "";
    public string EndStop { get; init; } = "";
    public DateTime ScheduledDeparture { get; init; }
    public DateTime ScheduledArrival { get; init; }
    public DateTime? ActualDeparture { get; init; }
    public DateTime? ActualArrival { get; init; }
    public int PlannedMinutes { get; init; }
    public int? DurationMinutes { get; init; }
    public string? Notes { get; init; }
    public string? CancelReason { get; init; }
    public bool CanStart { get; init; }
    public bool CanComplete { get; init; }
    public DateTime StartOpensAt { get; init; }
    public bool AssignmentActive { get; init; }
    public object? Driver { get; init; }
    public List<string>? Stops { get; init; }
    public DateTime UpdatedAt { get; init; }

    public static DriverTripViewModel From(DriverTrip t, DriverTripState? state = null, bool withDriver = false, bool withStops = false)
    {
        var stops = t.Bus?.Stops.OrderBy(s => s.Order).Select(s => s.Name).ToList();
        if (stops is not null && t.Direction == DriverTripDirection.Return)
        {
            stops.Reverse();
        }
        return new()
        {
            Id = t.Id.ToString(),
            Code = t.Code,
            TripNumber = t.TripNumber,
            ScheduleDate = t.ScheduleDate.ToString("yyyy-MM-dd"),
            Direction = t.Direction,
            Status = t.Status,
            RouteId = t.BusId.ToString(),
            BusName = t.Bus?.BusName ?? "",
            RouteNumber = t.Bus?.RouteNumber,
            Operator = t.Bus?.Operator,
            BusType = t.IndividualBus?.BusType ?? t.Bus?.BusType ?? "",
            Vehicle = DriverVehicleViewModel.From(t.IndividualBus),
            StartStop = t.StartStop,
            EndStop = t.EndStop,
            ScheduledDeparture = t.ScheduledDeparture,
            ScheduledArrival = t.ScheduledArrival,
            ActualDeparture = t.ActualDeparture,
            ActualArrival = t.ActualArrival,
            PlannedMinutes = (int)Math.Round((t.ScheduledArrival - t.ScheduledDeparture).TotalMinutes),
            DurationMinutes = t.DurationMinutes,
            Notes = t.Notes,
            CancelReason = t.CancelReason,
            CanStart = state?.CanStart ?? false,
            CanComplete = state?.CanComplete ?? false,
            StartOpensAt = state?.StartOpensAt ?? t.ScheduledDeparture,
            AssignmentActive = state?.AssignmentActive ?? true,
            Driver = withDriver && t.Driver is { } d ? new { id = d.Id, name = d.Name, phone = d.Phone ?? "", email = d.Email } : null,
            Stops = withStops ? stops : null,
            UpdatedAt = t.UpdatedAt,
        };
    }
}

public record WorkingStatusViewModel(string Status, DateTime? ChangedAt, string[] AllowedManual, bool HasActiveTrip)
{
    public static WorkingStatusViewModel From(DriverWorkingStatus s, bool activeTrip) => new(s.CurrentStatus,
        s.LastStatusChangedAt == DateTime.MinValue ? null : s.LastStatusChangedAt, activeTrip ? [] : DriverWorkStatus.Manual, activeTrip);
}

public record StatusHistoryViewModel(string? PreviousStatus, string NewStatus, string? Reason, string ChangedBy, DateTime ChangedAt)
{
    public static StatusHistoryViewModel From(DriverStatusHistory h) => new(h.PreviousStatus, h.NewStatus, h.Reason, h.ChangedBy, h.ChangedAt);
}
