using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace SmartCommute.Server.Services;

public record GeoPlace(string DisplayName, double Lat, double Lon, string PlaceId);

// Path is the road line as [lat, lon] points; it is empty for a straight-line estimate.
public record RoadDistance(double DistanceKm, double DurationMinutes, bool Estimated, IReadOnlyList<double[]> Path);

// Wraps OpenStreetMap Nominatim (place search) and OSRM (road distance).
// Nominatim's usage policy allows at most one request per second and asks clients to cache results, so every
// search goes through a shared one-at-a-time gate and repeated searches are answered from memory.
public class GeoService(HttpClient http, IOptions<GeoOptions> options, IMemoryCache cache, ILogger<GeoService> logger)
{
    // Rough bounding box of greater Dhaka (west, north, east, south), used to keep searches inside the city.
    public const string DhakaViewBox = "90.20,23.95,90.60,23.60";

    private static readonly TimeSpan SearchCacheFor = TimeSpan.FromHours(6);
    private static readonly TimeSpan RouteCacheFor = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan NominatimInterval = TimeSpan.FromMilliseconds(1100);
    private static readonly SemaphoreSlim NominatimGate = new(1, 1);
    private static DateTime _lastNominatimCall = DateTime.MinValue;

    private readonly GeoOptions _options = options.Value;

    // language: "en" or "bn" for the names in the results; Nominatim otherwise answers with local (Bangla) names.
    public async Task<List<GeoPlace>> SearchAsync(string query, int limit, string countryCodes, CancellationToken ct,
        bool dhakaOnly = false, string? language = null)
    {
        limit = Math.Clamp(limit, 1, 10);
        var key = $"geo:search:{query.Trim().ToLowerInvariant()}|{limit}|{countryCodes}|{dhakaOnly}|{language}";
        if (cache.TryGetValue(key, out List<GeoPlace>? cached) && cached is not null)
        {
            return cached;
        }

        var url = $"{_options.NominatimBaseUrl}/search?format=json&addressdetails=0" +
                  $"&q={Uri.EscapeDataString(query.Trim())}&limit={limit}" +
                  $"&countrycodes={Uri.EscapeDataString(countryCodes)}" +
                  (dhakaOnly ? $"&viewbox={DhakaViewBox}&bounded=1" : "") +
                  (language is "en" or "bn" ? $"&accept-language={language}" : "");

        string body;
        await NominatimGate.WaitAsync(ct);
        try
        {
            var wait = _lastNominatimCall + NominatimInterval - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct);
            }
            try
            {
                using var response = await http.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();
                body = await response.Content.ReadAsStringAsync(ct);
            }
            finally
            {
                _lastNominatimCall = DateTime.UtcNow;
            }
        }
        finally
        {
            NominatimGate.Release();
        }

        using var doc = JsonDocument.Parse(body);
        var places = new List<GeoPlace>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            places.Add(new GeoPlace(
                item.GetProperty("display_name").GetString() ?? "",
                double.Parse(item.GetProperty("lat").GetString()!, CultureInfo.InvariantCulture),
                double.Parse(item.GetProperty("lon").GetString()!, CultureInfo.InvariantCulture),
                item.GetProperty("place_id").ToString()));
        }
        cache.Set(key, places, SearchCacheFor);
        return places;
    }

    public async Task<RoadDistance> RoadDistanceAsync(double srcLat, double srcLon, double dstLat, double dstLon, CancellationToken ct)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"geo:route:{srcLat:F5},{srcLon:F5}:{dstLat:F5},{dstLon:F5}");
        if (cache.TryGetValue(key, out RoadDistance? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var coords = string.Create(CultureInfo.InvariantCulture, $"{srcLon},{srcLat};{dstLon},{dstLat}");
            var url = $"{_options.OsrmBaseUrl}/route/v1/driving/{coords}?overview=simplified&geometries=geojson";
            using var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var route = doc.RootElement.GetProperty("routes")[0];
            // GeoJSON points are [lon, lat]; the map wants [lat, lon].
            var path = route.TryGetProperty("geometry", out var geometry)
                ? geometry.GetProperty("coordinates").EnumerateArray()
                    .Select(p => new[] { p[1].GetDouble(), p[0].GetDouble() }).ToList()
                : [];
            var road = new RoadDistance(
                Math.Round(route.GetProperty("distance").GetDouble() / 1000, 2),
                Math.Round(route.GetProperty("duration").GetDouble() / 60, 1),
                Estimated: false,
                path);
            cache.Set(key, road, RouteCacheFor);
            return road;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException
                                       or IndexOutOfRangeException or InvalidOperationException
                                       or TaskCanceledException && !ct.IsCancellationRequested)
        {
            // Roads in Dhaka are roughly 1.3x the straight-line distance; good enough when OSRM is unavailable.
            // Not cached, so the next request tries OSRM again.
            logger.LogWarning(ex, "OSRM unavailable, using straight-line estimate");
            var km = Math.Round(HaversineKm(srcLat, srcLon, dstLat, dstLon) * 1.3, 2);
            return new RoadDistance(km, Math.Round(km / 20 * 60, 1), Estimated: true, []);
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
