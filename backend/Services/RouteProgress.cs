using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

public record ProgressStop(int Order, string Name, double Lat, double Lng, double KmFromStart);

public record RouteProgressResult(double TotalKm, double TravelledKm, double RemainingKm, double Percent, double OffRouteMeters,
    ProgressStop? CurrentStop, ProgressStop? LastPassedStop, ProgressStop? NextStop, List<ProgressStop> RemainingStops, List<ProgressStop> Stops,
    bool OnRoute);

// F23: how far along its route a bus is, from the route's own stops (in travel order) and the bus's position.
// The route is treated as straight lines between consecutive stops; the bus is projected onto the nearest of those
// lines, and the distance along the route to that point is the progress. Because real roads curve between stops,
// the figures are estimates, and the API says so.
public static class RouteProgress
{
    // Within this distance of a stop the bus counts as "at" it.
    public const double AtStopKm = 0.15;

    // Further than this from the route line the bus is reported as off the route (still estimated, never hidden).
    public const double OffRouteKm = 0.6;

    public static List<BusStop> InTravelOrder(Bus bus, string direction)
    {
        var stops = bus.Stops.OrderBy(s => s.Order).ToList();
        if (direction == DriverTripDirection.Return)
        {
            stops.Reverse();
        }
        return stops;
    }

    public static RouteProgressResult? Compute(IReadOnlyList<BusStop> stops, double lat, double lng)
    {
        var usable = stops.Where(s => double.IsFinite(s.Lat) && double.IsFinite(s.Lng) && !(s.Lat == 0 && s.Lng == 0)).ToList();
        if (usable.Count < 2)
        {
            return null;
        }

        // Distance from the start to each stop, along the route.
        var at = new double[usable.Count];
        for (var i = 1; i < usable.Count; i++)
        {
            at[i] = at[i - 1] + GeoService.HaversineKm(usable[i - 1].Lat, usable[i - 1].Lng, usable[i].Lat, usable[i].Lng);
        }
        var total = at[^1];
        var list = usable.Select((s, i) => new ProgressStop(i, s.Name, s.Lat, s.Lng, Math.Round(at[i], 3))).ToList();
        if (total <= 0)
        {
            return null;
        }

        // A flat projection around the bus is accurate to well under a metre over a city.
        var kx = 111.320 * Math.Cos(lat * Math.PI / 180);
        const double ky = 110.574;
        (double X, double Y) P(double la, double lo) => ((lo - lng) * kx, (la - lat) * ky);

        var bestDistance = double.MaxValue;
        var bestAlong = 0.0;
        for (var i = 0; i < usable.Count - 1; i++)
        {
            var a = P(usable[i].Lat, usable[i].Lng);
            var b = P(usable[i + 1].Lat, usable[i + 1].Lng);
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var lengthSq = dx * dx + dy * dy;
            // The bus is at (0, 0) in this projection.
            var t = lengthSq == 0 ? 0 : Math.Clamp((-a.X * dx - a.Y * dy) / lengthSq, 0, 1);
            var px = a.X + t * dx;
            var py = a.Y + t * dy;
            var distance = Math.Sqrt(px * px + py * py);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestAlong = at[i] + t * (at[i + 1] - at[i]);
            }
        }

        var travelled = Math.Clamp(bestAlong, 0, total);
        var current = list.Where(s => Math.Abs(s.KmFromStart - travelled) <= AtStopKm)
            .OrderBy(s => Math.Abs(s.KmFromStart - travelled)).FirstOrDefault();
        var lastPassed = list.LastOrDefault(s => s.KmFromStart <= travelled + 0.001);
        var next = list.FirstOrDefault(s => s.KmFromStart > travelled + 0.001 && s != current);
        var remaining = list.Where(s => s.KmFromStart > travelled + 0.001 && s != current).ToList();
        return new RouteProgressResult(Math.Round(total, 2), Math.Round(travelled, 2), Math.Round(total - travelled, 2),
            Math.Round(travelled / total * 100, 1), Math.Round(bestDistance * 1000), current, lastPassed, next, remaining, list, bestDistance <= OffRouteKm);
    }

    // "live" (fresh), "delayed" or "unavailable" (last known only), from the time of the last fix.
    public static string Freshness(DateTime? lastUpdated, DateTime now, LiveLocationOptions options)
    {
        if (lastUpdated is not { } last)
        {
            return "unavailable";
        }
        var age = (now - last).TotalSeconds;
        return age <= options.LiveSeconds ? "live" : age <= options.DelayedSeconds ? "delayed" : "unavailable";
    }
}
