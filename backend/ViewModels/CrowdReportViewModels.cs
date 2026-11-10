using System.ComponentModel.DataAnnotations;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Server.ViewModels;

// F19: what the crowd report pages read and send. Public views never carry who made a report, only whether it is
// the viewer's own.

public class CrowdReportViewModel
{
    public string Id { get; init; } = "";
    public string BusId { get; init; } = "";
    public string BusName { get; init; } = "";
    public string? RouteNumber { get; init; }
    public string RouteName { get; init; } = "";
    public string BusStopId { get; init; } = "";
    public string StopName { get; init; } = "";
    public string TravelDirection { get; init; } = "";
    public string TowardsStop { get; init; } = "";
    public string CrowdLevel { get; init; } = "";
    public string SeatAvailability { get; init; } = "";
    public int? AvailableSeatCount { get; init; }
    public int? StandingPassengerCount { get; init; }
    public string? AdditionalComments { get; init; }

    // UTC; ObservedAtDhaka is the same moment written in Dhaka time ("2026-11-10T08:30+06:00").
    public DateTime ObservedAt { get; init; }
    public string ObservedAtDhaka { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    // Worked out from the observation time when the response was made; pages keep it up to date as time passes.
    public int AgeMinutes { get; init; }
    public string Freshness { get; init; } = "";
    public bool IsCurrent { get; init; }

    public bool IsMine { get; init; }
    public bool CanEdit { get; init; }
    public DateTime? EditableUntil { get; init; }

    public static CrowdReportViewModel From(BusCrowdReport r, Guid viewerId, DateTime nowUtc)
    {
        var mine = viewerId != Guid.Empty && r.UserId == viewerId;
        var editableUntil = r.CreatedAt.AddMinutes(BusCrowdReportService.EditWindowMinutes);
        var freshness = CrowdFreshness.Of(r.ObservationDateTime, nowUtc);
        return new()
        {
            Id = r.Id.ToString(), BusId = r.BusId.ToString(), BusName = r.BusName, RouteNumber = r.RouteNumber, RouteName = r.RouteName,
            BusStopId = r.BusStopId.ToString(), StopName = r.StopName, TravelDirection = r.TravelDirection, TowardsStop = r.TowardsStop,
            CrowdLevel = r.CrowdLevel, SeatAvailability = r.SeatAvailability, AvailableSeatCount = r.AvailableSeatCount,
            StandingPassengerCount = r.StandingPassengerCount, AdditionalComments = r.AdditionalComments,
            ObservedAt = DateTime.SpecifyKind(r.ObservationDateTime, DateTimeKind.Utc), ObservedAtDhaka = DhakaTime.Format(r.ObservationDateTime),
            CreatedAt = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc), UpdatedAt = DateTime.SpecifyKind(r.UpdatedAt, DateTimeKind.Utc),
            AgeMinutes = (int)Math.Max(0, Math.Floor((nowUtc - r.ObservationDateTime).TotalMinutes)),
            Freshness = freshness, IsCurrent = freshness != CrowdFreshness.Expired,
            IsMine = mine, CanEdit = mine && nowUtc < editableUntil, EditableUntil = mine ? DateTime.SpecifyKind(editableUntil, DateTimeKind.Utc) : null,
        };
    }
}

// The latest current report about one route, with how many current reports it has.
public class CrowdBusSummaryViewModel
{
    public string BusId { get; init; } = "";
    public string BusName { get; init; } = "";
    public string? RouteNumber { get; init; }
    public string RouteName { get; init; } = "";
    public int ReportCount { get; init; }
    public CrowdReportViewModel Latest { get; init; } = new();
}

// The user always comes from the sign-in token, never from this body. observationDate and observationTime are Dhaka
// time ("2026-11-10", "08:30").
public class CrowdReportRequest
{
    [Required(ErrorMessage = "Choose the bus route.")]
    public Guid? BusId { get; set; }

    [Required(ErrorMessage = "Choose the stop where you saw the bus.")]
    public Guid? BusStopId { get; set; }

    [Required(ErrorMessage = "Choose the direction the bus was going."), StringLength(10)]
    public string TravelDirection { get; set; } = "";

    [Required(ErrorMessage = "Choose how crowded the bus was."), StringLength(10)]
    public string CrowdLevel { get; set; } = "";

    [Required(ErrorMessage = "Choose whether seats were free."), StringLength(12)]
    public string SeatAvailability { get; set; } = "";

    [Required(ErrorMessage = "Choose the date you saw the bus."), StringLength(10)]
    public string ObservationDate { get; set; } = "";

    [Required(ErrorMessage = "Choose the time you saw the bus."), StringLength(8)]
    public string ObservationTime { get; set; } = "";

    [Range(0, 100, ErrorMessage = "Free seats must be between 0 and 100.")]
    public int? AvailableSeatCount { get; set; }

    [Range(0, 200, ErrorMessage = "Standing passengers must be between 0 and 200.")]
    public int? StandingPassengerCount { get; set; }

    [StringLength(500, ErrorMessage = "Comments can be at most 500 characters.")]
    public string? AdditionalComments { get; set; }
}
