using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Extensions;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

// Bus stops: the full list, the stops near a point, name search and looking up one stop's position.
public class StopService(AppDbContext db, BusRouteCache routes)
{
    // Every bus stop once, by name, with its coordinates. A stop served by several routes is stored once per route,
    // so its position is the average of those rows.
    public async Task<List<StopPointViewModel>> AllAsync(CancellationToken ct) =>
        (await routes.AllAsync(ct))
            .SelectMany(r => r.Stops)
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new StopPointViewModel(g.First().Name, g.Average(s => s.Lat), g.Average(s => s.Lng)))
            .OrderBy(s => s.Name)
            .ToList();

    // Bus stops closest to a point, nearest first, with the straight-line distance to each.
    public async Task<List<NearbyStopViewModel>> NearbyAsync(double lat, double lng, double radiusKm, int limit, CancellationToken ct)
    {
        var radius = Math.Clamp(radiusKm, 0.1, 10);
        return (await AllAsync(ct))
            .Select(s => new NearbyStopViewModel(s.Name, s.Lat, s.Lng, Math.Round(GeoService.HaversineKm(lat, lng, s.Lat, s.Lng), 2)))
            .Where(s => s.DistanceKm <= radius)
            .OrderBy(s => s.DistanceKm)
            .Take(Math.Clamp(limit, 1, 20))
            .ToList();
    }

    // Stops whose name contains the text, names that start with it first.
    public async Task<List<StopPointViewModel>> SearchAsync(string text)
    {
        var term = text.Trim();
        var pattern = term.ContainsPattern();
        var stops = await db.BusStops
            .Where(s => EF.Functions.ILike(s.Name, pattern))
            .GroupBy(s => s.Name)
            .Select(g => new { Name = g.Key, Lat = g.Average(s => s.Lat), Lng = g.Average(s => s.Lng) })
            .ToListAsync();
        return stops
            .OrderByDescending(s => s.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            .ThenBy(s => s.Name)
            .Take(10)
            .Select(s => new StopPointViewModel(s.Name, s.Lat, s.Lng))
            .ToList();
    }

    // One stop by exact name, or else the shortest name containing it.
    public async Task<StopPointViewModel?> FindAsync(string name)
    {
        var exact = name.EscapeLike();
        var contains = name.ContainsPattern();
        var stop = await db.BusStops.FirstOrDefaultAsync(s => EF.Functions.ILike(s.Name, exact))
                   ?? await db.BusStops.OrderBy(s => s.Name.Length).FirstOrDefaultAsync(s => EF.Functions.ILike(s.Name, contains));
        return stop is null ? null : new StopPointViewModel(stop.Name, stop.Lat, stop.Lng);
    }
}
