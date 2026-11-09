using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Server.ViewModels;

// ---- Requests of the Smart Favourite Bus module. Ids are read as text so a bad id gets a clear message. ----

public static class Ids
{
    public static Guid Parse(string? text) => Guid.TryParse(text?.Trim(), out var id) ? id : Guid.Empty;
}

public class FavouriteCollectionRequest
{
    [Required(ErrorMessage = "Give the collection a name.")]
    [StringLength(FavoriteCollection.MaxNameLength, ErrorMessage = "Keep the collection name under 60 characters.")]
    public string? Name { get; set; }
}

public class FavouriteCollectionBusRequest
{
    [Required(ErrorMessage = "busId is required.")]
    public string? BusId { get; set; }

    // Moves the bus out of this collection of the user's in the same step.
    public string? FromCollectionId { get; set; }
}

public class FavouriteCompareRequest
{
    public List<string>? BusIds { get; set; }

    // Optional trip, to find which bus suits it best.
    [StringLength(100)]
    public string? From { get; set; }

    [StringLength(100)]
    public string? To { get; set; }
}

public class FavouriteBusIdRequest
{
    [Required(ErrorMessage = "busId is required.")]
    public string? BusId { get; set; }
}

public class AvailabilityAlertRequest
{
    public string? BusId { get; set; }

    [StringLength(100)]
    public string? FromStop { get; set; }

    [StringLength(100)]
    public string? ToStop { get; set; }

    public bool? IsActive { get; set; }
}

public class BusReminderRequest
{
    public string? BusId { get; set; }

    [StringLength(100)]
    public string? StopName { get; set; }

    // Bangladesh time, e.g. "2026-11-10T08:30" (an explicit offset is also accepted).
    public string? DepartureAt { get; set; }

    public int? LeadMinutes { get; set; }
    public bool? IsEnabled { get; set; }
}

// ---- Responses ----

public class FavouriteCollectionViewModel : EntityViewModel
{
    public string Name { get; init; } = "";
    public int BusCount { get; init; }
    public List<FavoriteBusViewModel> Buses { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public class FareHistoryEntryViewModel
{
    [JsonPropertyName("base_fare")]
    public decimal BaseFare { get; init; }

    [JsonPropertyName("per_km_fare")]
    public decimal PerKmFare { get; init; }

    [JsonPropertyName("previous_base_fare")]
    public decimal? PreviousBaseFare { get; init; }

    [JsonPropertyName("previous_per_km_fare")]
    public decimal? PreviousPerKmFare { get; init; }

    public DateTime RecordedAt { get; init; }

    // "baseline" (the fare when tracking started), "increased", "decreased" or "changed" (per-km fare only).
    public string Change { get; init; } = "baseline";

    public static FareHistoryEntryViewModel From(BusFareHistory h) => new()
    {
        BaseFare = h.BaseFare,
        PerKmFare = h.PerKmFare,
        PreviousBaseFare = h.PreviousBaseFare,
        PreviousPerKmFare = h.PreviousPerKmFare,
        RecordedAt = h.RecordedAt,
        Change = h.PreviousBaseFare is not { } before ? (h.PreviousPerKmFare is null ? "baseline" : "changed")
            : h.BaseFare > before ? "increased" : h.BaseFare < before ? "decreased" : "changed",
    };
}

public class AvailabilityAlertViewModel : EntityViewModel
{
    public string BusId { get; init; } = "";
    public string BusName { get; init; } = "";
    public string? RouteNumber { get; init; }
    public string FromStop { get; init; } = "";
    public string ToStop { get; init; } = "";
    public bool IsActive { get; init; }
    public int? LastSeatsFree { get; init; }
    public DateTime? LastNotifiedAt { get; init; }
    public DateTime CreatedAt { get; init; }

    public static AvailabilityAlertViewModel From(BusAvailabilityAlert a, BusViewModel? bus) => new()
    {
        Id = a.Id.ToString(),
        BusId = a.BusId.ToString(),
        BusName = bus?.BusName ?? "",
        RouteNumber = bus?.RouteNumber,
        FromStop = a.FromStop,
        ToStop = a.ToStop,
        IsActive = a.IsActive,
        LastSeatsFree = a.LastSeatsFree,
        LastNotifiedAt = a.LastNotifiedAt,
        CreatedAt = a.CreatedAt,
    };
}

public class BusReminderViewModel : EntityViewModel
{
    public string BusId { get; init; } = "";
    public string BusName { get; init; } = "";
    public string? RouteNumber { get; init; }
    public string StopName { get; init; } = "";
    public DateTime DepartureAt { get; init; }

    // The same times in Bangladesh Standard Time, e.g. "2026-11-10T08:30+06:00".
    public string DepartureAtLocal { get; init; } = "";
    public string RemindAtLocal { get; init; } = "";
    public int LeadMinutes { get; init; }
    public bool IsEnabled { get; init; }
    public DateTime? SentAt { get; init; }

    // "upcoming", "sent", "past" or "disabled".
    public string Status { get; init; } = "";

    public static BusReminderViewModel From(BusReminder r, BusViewModel? bus, DateTime now) => new()
    {
        Id = r.Id.ToString(),
        BusId = r.BusId.ToString(),
        BusName = bus?.BusName ?? "",
        RouteNumber = bus?.RouteNumber,
        StopName = r.StopName,
        DepartureAt = r.DepartureAt,
        DepartureAtLocal = DhakaTime.Format(r.DepartureAt),
        RemindAtLocal = DhakaTime.Format(r.DepartureAt.AddMinutes(-r.LeadMinutes)),
        LeadMinutes = r.LeadMinutes,
        IsEnabled = r.IsEnabled,
        SentAt = r.SentAt,
        Status = !r.IsEnabled ? "disabled" : r.SentAt is not null ? "sent" : r.DepartureAt <= now ? "past" : "upcoming",
    };
}
