using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace SmartCommute.Server.Services;

// A driving route from Google: road distance, travel time and the line to draw on the map.
// DurationMinutes includes current traffic; StaticDurationMinutes is the same trip without traffic.
public record RouteEstimate(double DistanceKm, double DurationMinutes, double? StaticDurationMinutes, string? Polyline);

// A Google request that failed, with the status code the API should answer with and a message for the user.
public class GoogleRoutesException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

// Calls the Google Maps Routes API (computeRoutes) with the server key. The key never reaches the browser.
// Results are cached for a few minutes so repeated searches for the same trip are not billed again.
public class GoogleRoutesService(HttpClient http, IOptions<GoogleMapsOptions> options, IMemoryCache cache, ILogger<GoogleRoutesService> logger)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);
    private const string FieldMask = "routes.distanceMeters,routes.duration,routes.staticDuration,routes.polyline.encodedPolyline";

    private readonly GoogleMapsOptions _options = options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ServerApiKey);

    public async Task<RouteEstimate> DrivingRouteAsync(double fromLat, double fromLng, double toLat, double toLng, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            throw new GoogleRoutesException("Distance is unavailable: the Google Maps server key is not configured.", StatusCodes.Status503ServiceUnavailable);
        }

        var key = string.Create(CultureInfo.InvariantCulture, $"route:{fromLat:F5},{fromLng:F5}:{toLat:F5},{toLng:F5}");
        if (cache.TryGetValue(key, out RouteEstimate? cached) && cached is not null)
        {
            return cached;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.RoutesBaseUrl.TrimEnd('/')}/directions/v2:computeRoutes")
        {
            Content = JsonContent.Create(new
            {
                origin = Waypoint(fromLat, fromLng),
                destination = Waypoint(toLat, toLng),
                travelMode = "DRIVE",
                routingPreference = "TRAFFIC_AWARE",
                regionCode = "BD",
                units = "METRIC",
            }),
        };
        request.Headers.Add("X-Goog-Api-Key", _options.ServerApiKey);
        request.Headers.Add("X-Goog-FieldMask", FieldMask);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Google Routes API unreachable");
            throw new GoogleRoutesException("Distance is temporarily unavailable: Google Maps could not be reached.", StatusCodes.Status502BadGateway);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Google Routes API returned {Status}: {Body}", (int)response.StatusCode, body);
                throw response.StatusCode switch
                {
                    HttpStatusCode.TooManyRequests => new GoogleRoutesException("Distance is temporarily unavailable: Google Maps is busy. Try again in a minute.", StatusCodes.Status503ServiceUnavailable),
                    HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest =>
                        new GoogleRoutesException("Distance is unavailable: Google Maps rejected the server key. Check the key and that the Routes API is enabled.", StatusCodes.Status502BadGateway),
                    _ => new GoogleRoutesException("Distance is temporarily unavailable: Google Maps returned an error.", StatusCodes.Status502BadGateway),
                };
            }

            var estimate = Parse(body) ?? throw new GoogleRoutesException("Google Maps found no road route between these places.", StatusCodes.Status404NotFound);
            cache.Set(key, estimate, CacheFor);
            return estimate;
        }
    }

    private static object Waypoint(double lat, double lng) => new { location = new { latLng = new { latitude = lat, longitude = lng } } };

    // An empty object ({}) means Google found no route.
    private static RouteEstimate? Parse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0)
        {
            return null;
        }
        var route = routes[0];
        var meters = route.TryGetProperty("distanceMeters", out var d) ? d.GetDouble() : 0;
        var duration = Minutes(route, "duration") ?? 0;
        var polyline = route.TryGetProperty("polyline", out var p) && p.TryGetProperty("encodedPolyline", out var e) ? e.GetString() : null;
        return new RouteEstimate(Math.Round(meters / 1000, 1), Math.Round(duration), Minutes(route, "staticDuration") is { } s ? Math.Round(s) : null, polyline);
    }

    // Durations come as seconds with an "s" suffix, e.g. "1534s".
    private static double? Minutes(JsonElement route, string property) =>
        route.TryGetProperty(property, out var value) && value.GetString() is { } text &&
        double.TryParse(text.TrimEnd('s'), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? seconds / 60
            : null;
}
