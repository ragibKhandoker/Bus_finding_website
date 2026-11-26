using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

// One bus on the live map, as commuters see it. Never carries the driver's identity.
public record LiveBus(Guid SessionId, Guid BusId, Guid? VehicleId, Guid TripId, string BusName, string? RouteNumber, string? Operator, string BusType,
    string? RegistrationNumber, string Direction, string From, string To, double? Latitude, double? Longitude, double? Accuracy, double? Speed,
    double? Heading, DateTime? LastUpdatedAt, DateTime StartedAt, bool IsSharing);

public record LiveLocationUpdate(double? Latitude, double? Longitude, double? Accuracy, double? Speed, double? Heading);

// What the driver's dashboard needs: the trip they could share, and their sharing state.
public record DriverLiveState(DriverTrip? Trip, DriverBusAssignment? Assignment, LiveLocation? Sharing, string? CannotStartReason);

// F23: drivers share their live GPS position during a trip; commuters see it on the map. Only an approved driver
// with a trip in progress on a bus they are actively assigned to can share, and only for that trip (the driver
// is the user in the token; nothing in the request says which bus). Commuters only ever see sessions that are
// sharing during a trip in progress; when sharing stops, the position is kept for the record but no longer shown.
// The list of active buses is read from the database at most every few seconds and shared by every commuter.
public class LiveLocationService(AppDbContext db, IMemoryCache cache, BusRouteCache routes, IOptions<LiveLocationOptions> options)
{
    private const string ActiveCacheKey = "live-location:active";
    private LiveLocationOptions Options => options.Value;
    public LiveLocationOptions Settings => Options;

    // ---- Driver ----

    public async Task<DriverLiveState> StateAsync(Guid driverId)
    {
        var trip = await db.DriverTrips.Include(t => t.Bus).Include(t => t.IndividualBus).AsNoTracking()
            .FirstOrDefaultAsync(t => t.DriverId == driverId && t.Status == DriverTripStatus.InProgress);
        var assignment = await db.DriverBusAssignments.Include(a => a.Bus).Include(a => a.IndividualBus).AsNoTracking()
            .Where(a => a.DriverId == driverId && a.IsActive && (trip == null || a.BusId == trip.BusId))
            .OrderByDescending(a => a.AssignedAt).FirstOrDefaultAsync();
        var sharing = trip is null ? null : await db.LiveLocations.AsNoTracking().FirstOrDefaultAsync(l => l.TripId == trip.Id);
        var reason = assignment is null ? "No bus has been assigned to your account yet."
            : trip is null ? "Start a trip first: live location is shared only while a trip is in progress."
            : null;
        return new DriverLiveState(trip, assignment, sharing, reason);
    }

    public async Task<ServiceResult<LiveLocation>> StartAsync(Guid driverId, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var trip = await db.DriverTrips.AsNoTracking().FirstOrDefaultAsync(t => t.DriverId == driverId && t.Status == DriverTripStatus.InProgress);
        if (trip is null)
        {
            return ServiceResult<LiveLocation>.Conflict("Start a trip first: live location is shared only while a trip is in progress.");
        }
        var assignment = await db.DriverBusAssignments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.DriverId == driverId && a.BusId == trip.BusId && a.IsActive);
        if (assignment is null)
        {
            return ServiceResult<LiveLocation>.Forbidden("You are not assigned to this bus, so you cannot share its location.");
        }
        var row = await db.LiveLocations.FirstOrDefaultAsync(l => l.TripId == trip.Id);
        if (row is null)
        {
            row = new LiveLocation { DriverId = driverId, TripId = trip.Id, BusId = trip.BusId, StartedAt = now };
            db.LiveLocations.Add(row);
        }
        else if (!row.IsSharing)
        {
            // Resuming during the same trip: the old fix is not shown until a new one arrives.
            row.StartedAt = now;
            row.Latitude = row.Longitude = row.Accuracy = row.Speed = row.Heading = null;
            row.LastUpdatedAt = null;
        }
        row.IndividualBusId = trip.IndividualBusId ?? assignment.IndividualBusId;
        row.IsSharing = true;
        row.StoppedAt = null;
        row.StopReason = null;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Started twice at the same moment (two tabs): the other request created the row.
            db.ChangeTracker.Clear();
            row = await db.LiveLocations.FirstAsync(l => l.TripId == trip.Id);
        }
        cache.Remove(ActiveCacheKey);
        return ServiceResult<LiveLocation>.Ok(row, "Live location sharing started.");
    }

    // A problem with the coordinates, or null when they can be used.
    public static string? CoordinateProblem(LiveLocationUpdate u, LiveLocationOptions o)
    {
        if (u.Latitude is not { } lat || u.Longitude is not { } lng || !double.IsFinite(lat) || !double.IsFinite(lng))
        {
            return "Latitude and longitude are required.";
        }
        if (lat is < -90 or > 90 || lng is < -180 or > 180)
        {
            return "Those coordinates are not valid.";
        }
        if (lat < o.MinLatitude || lat > o.MaxLatitude || lng < o.MinLongitude || lng > o.MaxLongitude)
        {
            return "This location is outside Dhaka. Live tracking covers Dhaka bus routes only.";
        }
        if (u.Accuracy is { } acc && (!double.IsFinite(acc) || acc < 0 || acc > o.MaxAccuracyMeters))
        {
            return $"The GPS fix is not precise enough (more than {o.MaxAccuracyMeters:0} m). Move to an open area and try again.";
        }
        if (u.Speed is { } speed && (!double.IsFinite(speed) || speed < 0 || speed > 150))
        {
            return "Speed must be between 0 and 150 km/h.";
        }
        if (u.Heading is { } heading && (!double.IsFinite(heading) || heading < 0 || heading > 360))
        {
            return "Heading must be between 0 and 360 degrees.";
        }
        return null;
    }

    // throttled: true when the update came too soon after the last one and was not written.
    public async Task<ServiceResult<(LiveLocation Row, bool Throttled)>> UpdateAsync(Guid driverId, LiveLocationUpdate u, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        if (CoordinateProblem(u, Options) is { } problem)
        {
            return ServiceResult<(LiveLocation, bool)>.BadRequest(problem);
        }
        var row = await db.LiveLocations.Include(l => l.Trip).FirstOrDefaultAsync(l => l.DriverId == driverId && l.IsSharing);
        if (row is null)
        {
            return ServiceResult<(LiveLocation, bool)>.Conflict("Live location sharing is not on. Start it again from your dashboard.");
        }
        if (row.Trip is not { Status: DriverTripStatus.InProgress })
        {
            Stop(row, "trip-ended", now);
            await db.SaveChangesAsync();
            cache.Remove(ActiveCacheKey);
            return ServiceResult<(LiveLocation, bool)>.Conflict("Your trip has ended, so live location sharing was stopped.");
        }
        if (row.LastUpdatedAt is { } last && (now - last).TotalSeconds < Options.MinUpdateIntervalSeconds)
        {
            return ServiceResult<(LiveLocation, bool)>.Ok((row, true), "Location received.");
        }
        row.Latitude = Math.Round(u.Latitude!.Value, 7);
        row.Longitude = Math.Round(u.Longitude!.Value, 7);
        row.Accuracy = u.Accuracy is { } a ? Math.Round(a, 1) : null;
        row.Speed = u.Speed is { } s ? Math.Round(s, 1) : null;
        row.Heading = u.Heading is { } h ? Math.Round(h % 360, 1) : null;
        row.LastUpdatedAt = now;
        row.UpdateCount++;
        await db.SaveChangesAsync();
        return ServiceResult<(LiveLocation, bool)>.Ok((row, false), "Location updated.");
    }

    public async Task<ServiceResult<LiveLocation?>> StopAsync(Guid driverId, string reason = "driver", DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var rows = await db.LiveLocations.Where(l => l.DriverId == driverId && l.IsSharing).ToListAsync();
        foreach (var row in rows)
        {
            Stop(row, reason, now);
        }
        await db.SaveChangesAsync();
        cache.Remove(ActiveCacheKey);
        return ServiceResult<LiveLocation?>.Ok(rows.FirstOrDefault(), rows.Count == 0 ? "Live location sharing was already off." : "Live location sharing stopped.");
    }

    // Called when a trip is completed: sharing for it ends in the same transaction.
    public Task<int> StopForTripAsync(Guid tripId, string reason, DateTime now)
    {
        cache.Remove(ActiveCacheKey);
        return db.LiveLocations.Where(l => l.TripId == tripId && l.IsSharing).ExecuteUpdateAsync(s => s
            .SetProperty(l => l.IsSharing, false).SetProperty(l => l.StoppedAt, now).SetProperty(l => l.StopReason, reason));
    }

    private static void Stop(LiveLocation row, string reason, DateTime now)
    {
        row.IsSharing = false;
        row.StoppedAt = now;
        row.StopReason = reason;
    }

    // ---- Commuters ----

    // Every sharing session of a trip in progress. No recency limit here; callers decide. Filters go on the rows,
    // before the projection, so they translate to SQL.
    private IQueryable<LiveLocation> SharingRows(DateTime? since = null) => db.LiveLocations.AsNoTracking()
        .Where(l => l.IsSharing && l.Trip!.Status == DriverTripStatus.InProgress && (since == null || l.LastUpdatedAt >= since));

    private static readonly System.Linq.Expressions.Expression<Func<LiveLocation, LiveBus>> Project = l => new LiveBus(l.Id, l.BusId, l.IndividualBusId, l.TripId, l.Bus!.BusName, l.Bus.RouteNumber, l.Bus.Operator,
            l.IndividualBus != null ? l.IndividualBus.BusType : l.Bus.BusType, l.IndividualBus != null ? l.IndividualBus.BusCode : null,
            l.Trip!.Direction, l.Trip.StartStop, l.Trip.EndStop, l.Latitude, l.Longitude, l.Accuracy, l.Speed, l.Heading, l.LastUpdatedAt, l.StartedAt, l.IsSharing);

    // Buses with a recent fix, for the live map. One database read every few seconds, however many commuters ask.
    public async Task<List<LiveBus>> ActiveAsync(DateTime now)
    {
        if (cache.TryGetValue(ActiveCacheKey, out List<LiveBus>? hit) && hit is not null)
        {
            return hit;
        }
        var since = now.AddMinutes(-Options.ActiveListMinutes);
        var list = await SharingRows(since).Where(l => l.Latitude != null).OrderByDescending(l => l.LastUpdatedAt).ThenBy(l => l.Id)
            .Take(500).Select(Project).ToListAsync();
        cache.Set(ActiveCacheKey, list, TimeSpan.FromSeconds(Math.Max(1, Options.CacheSeconds)));
        return list;
    }

    // id: a vehicle id or a route (Bus) id; session picks one exact sharing session. A vehicle match wins over a
    // route match, then the most recent fix. Null when nothing is sharing for it right now.
    public async Task<LiveBus?> FindAsync(Guid id, Guid? session)
    {
        var query = SharingRows().Where(l => l.IndividualBusId == id || l.BusId == id || l.Id == id);
        if (session is { } s)
        {
            query = query.Where(l => l.Id == s);
        }
        return await query.OrderByDescending(l => l.IndividualBusId == id).ThenByDescending(l => l.LastUpdatedAt).ThenBy(l => l.Id)
            .Select(Project).FirstOrDefaultAsync();
    }

    // The route of a bus or vehicle id, for showing it even when the bus is not sharing.
    public async Task<(Guid BusId, string BusName, string? RouteNumber, string? Operator, string BusType, string? Registration)?> BusInfoAsync(Guid id)
    {
        var vehicle = await db.IndividualBuses.AsNoTracking().Where(v => v.Id == id)
            .Select(v => new { v.BusId, v.Bus!.BusName, v.Bus.RouteNumber, v.Bus.Operator, v.BusType, v.BusCode }).FirstOrDefaultAsync();
        if (vehicle is not null)
        {
            return (vehicle.BusId, vehicle.BusName, vehicle.RouteNumber, vehicle.Operator, vehicle.BusType, vehicle.BusCode);
        }
        var bus = await routes.ByIdAsync(id);
        return bus is null ? null : (id, bus.BusName, bus.RouteNumber, bus.Operator, bus.BusType, null);
    }

    public async Task<(BusViewModel Route, List<BusStop> Stops, RouteProgressResult? Progress)?> ProgressAsync(Guid busId, string direction, double? lat, double? lng)
    {
        var route = await routes.ByIdAsync(busId);
        if (route is null)
        {
            return null;
        }
        var bus = new Bus { Id = busId, Stops = route.Stops.Select((s, i) => new BusStop { Order = s.Order, Name = s.Name, Lat = s.Lat, Lng = s.Lng }).ToList() };
        var stops = RouteProgress.InTravelOrder(bus, direction);
        var progress = lat is { } la && lng is { } lo ? RouteProgress.Compute(stops, la, lo) : null;
        return (route, stops, progress);
    }
}
