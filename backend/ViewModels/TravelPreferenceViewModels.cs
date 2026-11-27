using System.ComponentModel.DataAnnotations;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.ViewModels;

// F19: what the travel preferences page sends and reads. The owner is never part of the request; it always comes
// from the sign-in token.

public class TravelPreferenceRequest
{
    [Required(ErrorMessage = "Give this preference a name."), StringLength(60, ErrorMessage = "The name can be at most 60 characters.")]
    public string PreferenceName { get; set; } = "";

    [Required(ErrorMessage = "Enter where you usually start."), StringLength(100, ErrorMessage = "The start location can be at most 100 characters.")]
    public string StartLocation { get; set; } = "";

    [Required(ErrorMessage = "Enter where you usually go."), StringLength(100, ErrorMessage = "The destination can be at most 100 characters.")]
    public string Destination { get; set; } = "";

    [Required(ErrorMessage = "Choose the Dhaka bus you use most.")]
    public Guid? BusId { get; set; }

    // "HH:mm", 24-hour.
    [Required(ErrorMessage = "Choose your usual travel time."), StringLength(8)]
    public string PreferredTravelTime { get; set; } = "";

    [Required(ErrorMessage = "Choose how many days a week you travel.")]
    public int? WeeklyFrequency { get; set; }

    public List<string>? TravelDays { get; set; }

    [Required(ErrorMessage = "Choose the bus type you prefer."), StringLength(10)]
    public string BusType { get; set; } = "";

    [Required(ErrorMessage = "Choose why you make this journey."), StringLength(20)]
    public string TravelPurpose { get; set; } = "";
}

public class TravelPreferenceViewModel
{
    public string Id { get; init; } = "";
    public string PreferenceName { get; init; } = "";
    public string StartLocation { get; init; } = "";
    public string Destination { get; init; } = "";
    public string? BusId { get; init; }
    public string FrequentlyUsedBus { get; init; } = "";
    public string? BusRouteNumber { get; init; }
    public string PreferredTravelTime { get; init; } = "";
    public int WeeklyFrequency { get; init; }
    public List<string> TravelDays { get; init; } = [];
    public string BusType { get; init; } = "";
    public string TravelPurpose { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    public static TravelPreferenceViewModel From(TravelPreference p) => new()
    {
        Id = p.Id.ToString(), PreferenceName = p.PreferenceName, StartLocation = p.StartLocation, Destination = p.Destination,
        BusId = p.BusId?.ToString(), FrequentlyUsedBus = p.FrequentlyUsedBus, BusRouteNumber = p.BusRouteNumber,
        PreferredTravelTime = p.PreferredTravelTime.ToString("HH:mm"), WeeklyFrequency = p.WeeklyFrequency, TravelDays = p.TravelDays,
        BusType = p.BusType, TravelPurpose = p.TravelPurpose, CreatedAt = p.CreatedAt, UpdatedAt = p.UpdatedAt,
    };
}
