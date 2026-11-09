using System.Reflection;
using System.Text.Json;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

// Decides which bus routes belong to the Dhaka City network, for the Favourite Bus module. The buses table has no
// city field, so the decision is made from each route's own stops (see Data/dhaka-city-boundary.json): a route is a
// Dhaka City route only when it has at least two stops and every stop is inside the city bounds and not one of
// the stops known to lie outside the city corporations. One stop outside is enough to exclude the whole route,
// so a bus that runs on to Savar, Gazipur or Narayanganj is never treated as a city bus.
public sealed class DhakaCityArea
{
    public const string ResourceName = "SmartCommute.Server.Data.dhaka-city-boundary.json";

    public record Bounds(double MinLat, double MaxLat, double MinLng, double MaxLng)
    {
        public bool Contains(double lat, double lng) => lat >= MinLat && lat <= MaxLat && lng >= MinLng && lng <= MaxLng;
    }

    private readonly Bounds _bounds;
    private readonly HashSet<string> _outside;

    public DhakaCityArea(Bounds bounds, IEnumerable<string> outsideCityStops)
    {
        _bounds = bounds;
        _outside = new HashSet<string>(outsideCityStops.Select(BusRouteMatcher.Normalize), StringComparer.Ordinal);
    }

    // The boundary shipped with the server, read once.
    public static DhakaCityArea Default => _default.Value;
    private static readonly Lazy<DhakaCityArea> _default = new(Load);

    public static DhakaCityArea Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource {ResourceName}.");
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        var b = root.GetProperty("cityBounds");
        var bounds = new Bounds(b.GetProperty("minLat").GetDouble(), b.GetProperty("maxLat").GetDouble(), b.GetProperty("minLng").GetDouble(), b.GetProperty("maxLng").GetDouble());
        var outside = root.GetProperty("outsideCityStops").EnumerateObject()
            .SelectMany(area => area.Value.EnumerateArray().Select(name => name.GetString() ?? ""))
            .Where(name => name.Length > 0);
        return new DhakaCityArea(bounds, outside);
    }

    // Whether a point lies inside the Dhaka City bounds (used for ride share places, which have no stop name).
    public bool InCity(double lat, double lng) => _bounds.Contains(lat, lng);

    public bool IsCityStop(string name, double lat, double lng) =>
        _bounds.Contains(lat, lng) && !_outside.Contains(BusRouteMatcher.Normalize(name));

    public bool IsCityRoute(IReadOnlyCollection<(string Name, double Lat, double Lng)> stops) =>
        stops.Count >= 2 && stops.All(s => IsCityStop(s.Name, s.Lat, s.Lng));

    public bool IsCityRoute(Bus bus) => IsCityRoute(bus.Stops.Select(s => (s.Name, s.Lat, s.Lng)).ToList());

    public bool IsCityRoute(BusViewModel bus) => IsCityRoute(bus.Stops.Select(s => (s.Name, s.Lat, s.Lng)).ToList());

    // The message every Favourite Bus endpoint gives for a bus outside the city network.
    public const string OutsideMessage = "Only Dhaka City buses can be used here. This bus runs outside Dhaka City.";
}
