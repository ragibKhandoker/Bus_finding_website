using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SmartCommute.Server.Services;

public record GeoPlace(string DisplayName, double Lat, double Lon, string PlaceId);
public record RoadDistance(double DistanceKm, double DurationMinutes, bool Estimated);

// Wraps OpenStreetMap Nominatim (place search) and OSRM (road distance).
public class GeoService(HttpClient http, IOptions<GeoOptions> options, ILogger<GeoService> logger)
{
    private readonly GeoOptions _options = options.Value;

    public async Task<List<GeoPlace>> SearchAsync(string query, int limit, string countryCodes, CancellationToken ct)
    {
        var url = $"{_options.NominatimBaseUrl}/search?format=json&addressdetails=0" +
                  $"&q={Uri.EscapeDataString(query)}&limit={Math.Clamp(limit, 1, 10)}" +
                  $"&countrycodes={Uri.EscapeDataString(countryCodes)}";

        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

        var places = new List<GeoPlace>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            places.Add(new GeoPlace(
                item.GetProperty("display_name").GetString() ?? "",
                double.Parse(item.GetProperty("lat").GetString()!, CultureInfo.InvariantCulture),
                double.Parse(item.GetProperty("lon").GetString()!, CultureInfo.InvariantCulture),
                item.GetProperty("place_id").ToString()));
        }
        return places;
    }

    public async Task<RoadDistance> RoadDistanceAsync(double srcLat, double srcLon, double dstLat, double dstLon, CancellationToken ct)
    {
        try
        {
            var coords = string.Create(CultureInfo.InvariantCulture, $"{srcLon},{srcLat};{dstLon},{dstLat}");
            var url = $"{_options.OsrmBaseUrl}/route/v1/driving/{coords}?overview=false";
            using var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var route = doc.RootElement.GetProperty("routes")[0];
            return new RoadDistance(
                Math.Round(route.GetProperty("distance").GetDouble() / 1000, 2),
                Math.Round(route.GetProperty("duration").GetDouble() / 60, 1),
                Estimated: false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException
                                       or IndexOutOfRangeException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Roads in Dhaka are roughly 1.3x the straight-line distance; good enough when OSRM is unavailable.
            logger.LogWarning(ex, "OSRM unavailable, using straight-line estimate");
            var km = Math.Round(HaversineKm(srcLat, srcLon, dstLat, dstLon) * 1.3, 2);
            return new RoadDistance(km, Math.Round(km / 20 * 60, 1), Estimated: true);
        }
    }

    public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusKm = 6371;
        static double ToRad(double deg) => deg * Math.PI / 180;
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
