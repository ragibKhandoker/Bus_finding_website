using SmartCommute.Server.Services;

namespace SmartCommute.Tests;

// A valid Dhaka City ride for the integration tests: both places inside Dhaka with coordinates, leaving in three
// hours. Callers pick the destination's name (often made unique so lists can be checked exactly).
public static class RideTestData
{
    public const double MirpurLat = 23.8069, MirpurLng = 90.3687;
    public const double MotijheelLat = 23.7330, MotijheelLng = 90.4172;
    public const double SavarLat = 23.8583, SavarLng = 90.2667;

    public static string DepartureIn(TimeSpan fromNow) => DhakaTime.Format(DateTime.UtcNow + fromNow);

    public static Dictionary<string, object?> Body(string destination = "Motijheel", string preference = "Any", int maxParticipants = 3,
        string vehicle = "Car", decimal cost = 60, TimeSpan? leavesIn = null, string source = "Mirpur 10") => new()
    {
        ["source"] = source,
        ["sourceLat"] = MirpurLat,
        ["sourceLng"] = MirpurLng,
        ["destination"] = destination,
        ["destinationLat"] = MotijheelLat,
        ["destinationLng"] = MotijheelLng,
        ["departureAt"] = DepartureIn(leavesIn ?? TimeSpan.FromHours(3)),
        ["maxParticipants"] = maxParticipants,
        ["genderPreference"] = preference,
        ["vehicleType"] = vehicle,
        ["costPerPerson"] = cost,
    };

    public static Dictionary<string, object?> With(this Dictionary<string, object?> body, string key, object? value)
    {
        var copy = new Dictionary<string, object?>(body) { [key] = value };
        return copy;
    }

    public static Dictionary<string, object?> Without(this Dictionary<string, object?> body, string key)
    {
        var copy = new Dictionary<string, object?>(body);
        copy.Remove(key);
        return copy;
    }
}
