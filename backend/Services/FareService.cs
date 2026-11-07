using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartCommute.Server.Data;
using SmartCommute.Server.Extensions;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

// Distance-based fare estimates for the map's fare calculator: road distance from GeoService times the
// Rewards:FarePerKm rate, rounded up to whole taka like trip fares (see TripService.Fare).
public class FareService(AppDbContext db, GeoService geo, IOptions<RewardOptions> rewards)
{
    // The same calculation sent twice within this window (a double click, a retried request) is saved once.
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(60);

    public decimal FarePerKm => rewards.Value.FarePerKm;

    public bool IsConfigured => FarePerKm > 0;

    public static decimal FareFor(double km, decimal farePerKm) => Math.Ceiling((decimal)km * farePerKm);

    public decimal FareFor(double km) => FareFor(km, FarePerKm);

    // Works out the fare between two places and saves it. Returns the saved record, the road line for the map,
    // and whether a new record was created (false when an identical one was saved moments ago).
    public async Task<(FareRecord Record, RoadDistance Road, bool Created)> CalculateAndSaveAsync(
        Guid? userId, CalculateFareRequest request, CancellationToken ct)
    {
        var from = request.SourceCoordinates!;
        var to = request.DestinationCoordinates!;
        var road = await geo.RoadDistanceAsync(from.Lat, from.Lon, to.Lat, to.Lon, ct);

        var record = new FareRecord
        {
            UserId = userId,
            Source = request.Source.Trim(),
            Destination = request.Destination.Trim(),
            Distance = Math.Round(road.DistanceKm, 2),
            FarePerKm = FarePerKm,
            Fare = FareFor(road.DistanceKm),
            DistanceEstimated = road.Estimated,
            SourceLat = Math.Round(from.Lat, 6),
            SourceLon = Math.Round(from.Lon, 6),
            DestinationLat = Math.Round(to.Lat, 6),
            DestinationLon = Math.Round(to.Lon, 6),
        };

        var since = DateTime.UtcNow - DuplicateWindow;
        var duplicate = await db.Fares
            .Where(f => f.UserId == userId && f.CreatedAt >= since
                        && f.Source == record.Source && f.Destination == record.Destination
                        && f.SourceLat == record.SourceLat && f.SourceLon == record.SourceLon
                        && f.DestinationLat == record.DestinationLat && f.DestinationLon == record.DestinationLon
                        && f.FarePerKm == record.FarePerKm)
            .OrderByDescending(f => f.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (duplicate is not null)
        {
            return (duplicate, road, false);
        }

        db.Fares.Add(record);
        await db.SaveChangesAsync(ct);
        return (record, road, true);
    }

    public Task<List<FareRecord>> HistoryAsync(Guid userId, int limit, CancellationToken ct) =>
        db.Fares
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .AsNoTracking()
            .ToListAsync(ct);

    public Task<bool> UserExistsAsync(Guid userId) => db.Users.AnyAsync(u => u.Id == userId);

    // Saves a fare a client calculated itself (older clients). The fare is worked out here from the distance.
    public async Task<FareRecord> SaveAsync(SaveFareRequest request)
    {
        var fare = new FareRecord
        {
            UserId = request.UserId,
            Source = request.Source.Trim(),
            Destination = request.Destination.Trim(),
            Distance = Math.Round(request.Distance, 2),
            FarePerKm = FarePerKm,
            Fare = FareFor(request.Distance),
            SourceLat = request.SourceCoordinates?.Lat,
            SourceLon = request.SourceCoordinates?.Lon,
            DestinationLat = request.DestinationCoordinates?.Lat,
            DestinationLon = request.DestinationCoordinates?.Lon,
        };
        db.Fares.Add(fare);
        await db.SaveChangesAsync();
        return fare;
    }

    // The most recent saved fare for a route, preferring the user's own when a user is given.
    public async Task<FareRecord?> LatestForRouteAsync(string source, string destination, Guid? userId)
    {
        var src = source.EscapeLike();
        var dst = destination.EscapeLike();
        var query = db.Fares.Where(f => EF.Functions.ILike(f.Source, src) && EF.Functions.ILike(f.Destination, dst));
        return (userId is null ? null : await query.Where(f => f.UserId == userId).OrderByDescending(f => f.CreatedAt).FirstOrDefaultAsync())
               ?? await query.OrderByDescending(f => f.CreatedAt).FirstOrDefaultAsync();
    }

    public Task<List<FareRecord>> ForUserAsync(Guid userId) => db.Fares
        .Where(f => f.UserId == userId)
        .OrderByDescending(f => f.CreatedAt)
        .Take(100)
        .AsNoTracking()
        .ToListAsync();
}
