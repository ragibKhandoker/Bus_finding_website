using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

// What a client sends to post or edit a ride. Every field is checked by RideRules.Check; nothing is trusted.
// The departure is either DepartureDate ("2026-11-10") with DepartureTime ("08:30"), both Dhaka time, or one
// DepartureAt ("2026-11-10T08:30", Dhaka time unless it carries an offset).
public record RideInput(
    string? Source, double? SourceLat, double? SourceLng,
    string? Destination, double? DestinationLat, double? DestinationLng,
    string? DepartureDate, string? DepartureTime, string? DepartureAt,
    int? MaxParticipants, string? GenderPreference, string? VehicleType, decimal? CostPerPerson,
    string? Instructions, string? ContactPreference, string? PickupDetails);

// A place with its coordinates.
public record RidePlace(string Name, double Lat, double Lng);

// A ride's details after every check passed.
public record RideFields(
    RidePlace Source, RidePlace Destination, DateTime DepartureAtUtc, int MaxParticipants, string GenderPreference,
    string VehicleType, decimal CostPerPerson, string? Instructions, string ContactPreference, string? PickupDetails);

// The rules for a ride's details. Pure (no database), so they are unit tested directly. Places must lie inside
// Dhaka City (DhakaCityArea's bounds); a place sent without coordinates is looked up among the bus stops by the
// caller before Check runs (see RideShareService.ResolvePlaceAsync).
public static class RideRules
{
    public const int MaxPlaceLength = 200;
    public const int MaxInstructionsLength = 500;
    public const int MaxPickupLength = 300;
    public const decimal MaxCost = 10_000m;
    // How far ahead a ride can be posted.
    public static readonly TimeSpan MaxLeadTime = TimeSpan.FromDays(60);
    // A departure a few minutes in the past (the time it took to fill the form) is still accepted.
    public static readonly TimeSpan PastGrace = TimeSpan.FromMinutes(2);
    // Source and destination closer than this are the same place.
    public const double MinTripKm = 0.3;

    public const string OutsideDhakaMessage = "Rides are for Dhaka City only. Choose a place inside Dhaka City.";

    public static (RideFields? Fields, string? Error) Check(RideInput input, RidePlace? source, RidePlace? destination, DateTime nowUtc,
        int configuredMax, DhakaCityArea area)
    {
        var sourceName = input.Source?.Trim() ?? "";
        var destinationName = input.Destination?.Trim() ?? "";
        if (sourceName.Length == 0)
        {
            return (null, "Please enter where the ride starts.");
        }
        if (destinationName.Length == 0)
        {
            return (null, "Please enter where the ride goes.");
        }
        if (sourceName.Length > MaxPlaceLength || destinationName.Length > MaxPlaceLength)
        {
            return (null, "Places can be at most 200 characters.");
        }
        if (source is null || destination is null)
        {
            return (null, "Choose the start and the destination from the Dhaka location suggestions.");
        }
        if (!ValidCoordinate(source) || !ValidCoordinate(destination) || !area.InCity(source.Lat, source.Lng) || !area.InCity(destination.Lat, destination.Lng))
        {
            return (null, OutsideDhakaMessage);
        }
        if (string.Equals(BusRouteMatcher.Normalize(sourceName), BusRouteMatcher.Normalize(destinationName), StringComparison.Ordinal)
            || GeoService.HaversineKm(source.Lat, source.Lng, destination.Lat, destination.Lng) < MinTripKm)
        {
            return (null, "The start and the destination cannot be the same place.");
        }

        if (Departure(input) is not { } departure)
        {
            return (null, "Choose the departure date and time.");
        }
        if (departure < nowUtc - PastGrace)
        {
            return (null, "The departure time cannot be in the past.");
        }
        if (departure > nowUtc + MaxLeadTime)
        {
            return (null, "Rides can be posted at most 60 days ahead.");
        }

        if (RideVehicleTypes.Normalize(input.VehicleType) is not { } vehicle)
        {
            return (null, "Vehicle type must be Car, Bike, CNG or Other.");
        }
        var capacity = RideVehicleTypes.Capacity(vehicle, configuredMax);
        if (input.MaxParticipants is not { } max || max < 2 || max > configuredMax)
        {
            return (null, $"Maximum participants must be a whole number from 2 to {configuredMax}, counting you.");
        }
        if (max > capacity)
        {
            return (null, vehicle == RideVehicleTypes.Bike
                ? "A bike takes one rider, so maximum participants is 2, counting you."
                : $"A {vehicle} takes at most {capacity} people, counting you.");
        }
        if (RideGenderPreference.Normalize(input.GenderPreference) is not { } preference)
        {
            return (null, "Gender preference must be Any, Male or Female.");
        }
        if (input.CostPerPerson is not { } cost)
        {
            return (null, "Enter the estimated cost per person (0 if it is free).");
        }
        if (cost < 0)
        {
            return (null, "The estimated cost cannot be negative.");
        }
        if (cost > MaxCost || decimal.Round(cost, 2) != cost)
        {
            return (null, "Enter the cost per person in taka, up to 10,000 with at most 2 decimals.");
        }
        if (RideContactPreferences.Normalize(input.ContactPreference) is not { } contact)
        {
            return (null, "Contact preference must be chat, phone or both.");
        }
        var instructions = Optional(input.Instructions);
        if (instructions?.Length > MaxInstructionsLength)
        {
            return (null, "Additional instructions can be at most 500 characters.");
        }
        var pickup = Optional(input.PickupDetails);
        if (pickup?.Length > MaxPickupLength)
        {
            return (null, "Pickup details can be at most 300 characters.");
        }

        return (new RideFields(source with { Name = sourceName }, destination with { Name = destinationName }, departure, max, preference,
            vehicle, cost, instructions, contact, pickup), null);
    }

    // The departure in UTC, or null when it is missing or not a real date and time.
    public static DateTime? Departure(RideInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.DepartureAt))
        {
            return DhakaTime.ParseToUtc(input.DepartureAt);
        }
        if (string.IsNullOrWhiteSpace(input.DepartureDate) || string.IsNullOrWhiteSpace(input.DepartureTime))
        {
            return null;
        }
        return DhakaTime.ParseToUtc($"{input.DepartureDate.Trim()}T{input.DepartureTime.Trim()}");
    }

    // A place the client sent with coordinates; null when it sent none (then the caller looks the name up).
    public static RidePlace? FromInput(string? name, double? lat, double? lng) =>
        lat is { } la && lng is { } ln && !string.IsNullOrWhiteSpace(name) ? new RidePlace(name.Trim(), la, ln) : null;

    private static bool ValidCoordinate(RidePlace place) =>
        double.IsFinite(place.Lat) && double.IsFinite(place.Lng) && Math.Abs(place.Lat) <= 90 && Math.Abs(place.Lng) <= 180;

    private static string? Optional(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
