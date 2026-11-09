using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

// Comparison, fare tracking, recently viewed buses, the dashboard summary and the module's notifications. Every
// figure comes from the database (buses, stops, vehicles, fare history); what the data does not record is
// returned as null rather than guessed.
public partial class FavouriteInsightsService(AppDbContext db, BusRouteCache routes, DhakaBusGuard guard, FavouriteMonitor monitor)
{
    public const int MaxCompared = 3;

    // ---------------- Comparison ----------------

    public async Task<ServiceResult<object>> CompareAsync(Guid userId, FavouriteCompareRequest request, CancellationToken ct)
    {
        var ids = (request.BusIds ?? []).Select(Ids.Parse).ToList();
        if (ids.Any(id => id == Guid.Empty))
        {
            return ServiceResult<object>.BadRequest("One of the bus ids is not valid.");
        }
        ids = ids.Distinct().ToList();
        if (ids.Count < 2 || ids.Count > MaxCompared)
        {
            return ServiceResult<object>.BadRequest($"Choose 2 or {MaxCompared} of your favourite buses to compare.");
        }
        var buses = new List<BusViewModel>();
        foreach (var id in ids)
        {
            var check = await guard.FavouriteCityBusAsync(userId, id, ct);
            if (!check.Succeeded)
            {
                return check.As<object>();
            }
            buses.Add(check.Value!);
        }

        var seats = await FavouriteMonitor.FreeSeatsAsync(db, ids, ct);
        var vehicles = await db.IndividualBuses.Where(v => ids.Contains(v.BusId) && v.Status == FavouriteMonitor.ActiveStatus)
            .GroupBy(v => v.BusId).Select(g => new { BusId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.BusId, x => x.Count, ct);

        // The trip, when one was given: which of these buses serve it, and how.
        string? from = request.From?.Trim(), to = request.To?.Trim();
        var start = string.IsNullOrEmpty(from) ? null : BusRouteMatcher.Resolve(buses, from);
        var end = string.IsNullOrEmpty(to) ? null : BusRouteMatcher.Resolve(buses, to);

        var rows = buses.Select(bus =>
        {
            var id = Guid.Parse(bus.Id);
            var stops = bus.Stops;
            var lengthKm = stops.Count > 1 ? Math.Round(stops.Zip(stops.Skip(1), (a, b) => GeoService.HaversineKm(a.Lat, a.Lng, b.Lat, b.Lng)).Sum(), 1) : (double?)null;
            var match = start is not null && end is not null ? BusRouteMatcher.Match(bus, start, end) : null;
            var servesDestination = end is not null && stops.Any(s => end.Names.Contains(s.Name));
            double? rideKm = match is null ? null : Math.Round(match.Segment.Zip(match.Segment.Skip(1), (a, b) => GeoService.HaversineKm(a.Lat, a.Lng, b.Lat, b.Lng)).Sum(), 1);
            return new
            {
                busId = bus.Id,
                busName = bus.BusName,
                routeNumber = bus.RouteNumber,
                @operator = bus.Operator,
                startingPoint = stops.FirstOrDefault()?.Name,
                destination = stops.LastOrDefault()?.Name,
                route = stops.Select(s => s.Name).ToList(),
                totalStops = stops.Count,
                base_fare = bus.BaseFare,
                per_km_fare = bus.PerKmFare,
                busType = bus.BusType,
                // Only what the bus record says: the route data marks AC services in their name, e.g. "(AC)".
                airConditioning = AcFromName(bus.BusName),
                // The route data has a frequency for some buses but no timetable.
                departureInfo = bus.Frequency,
                vehiclesInService = vehicles.GetValueOrDefault(id),
                seatsFreeNow = seats.TryGetValue(id, out var free) ? free : (int?)null,
                routeLengthKm = lengthKm,
                runsBothWays = bus.RunsBothWays,
                servesDestination = end is null ? (bool?)null : servesDestination,
                trip = match is null ? null : new
                {
                    boarding = match.Boarding.Name,
                    alighting = match.Alighting.Name,
                    stopsToRide = match.StopsToRide,
                    distanceKm = rideKm,
                    fare = TripService.Fare(bus, rideKm ?? 0),
                },
            };
        }).ToList();

        // Highlights, only where the figure exists for every bus compared and one bus is strictly best.
        static string? Best<T>(IEnumerable<(string Id, T? Value)> values) where T : struct, IComparable<T>
        {
            var list = values.ToList();
            if (list.Any(v => v.Value is null)) return null;
            var min = list.Min(v => v.Value!.Value);
            var best = list.Where(v => v.Value!.Value.CompareTo(min) == 0).ToList();
            return best.Count == 1 ? best[0].Id : null;
        }
        var shortest = Best(rows.Select(r => (r.busId, r.routeLengthKm)));
        var lowestFare = Best(rows.Select(r => (r.busId, (decimal?)r.base_fare)));
        string? suitable = null, suitableReason = null;
        var serving = rows.Where(r => r.trip is not null).ToList();
        if (serving.Count > 0)
        {
            // Fewest stops to ride, then the lower fare for the trip.
            var pick = serving.OrderBy(r => r.trip!.stopsToRide).ThenBy(r => r.trip!.fare).First();
            suitable = pick.busId;
            suitableReason = $"Goes from {pick.trip!.boarding} to {pick.trip.alighting} in {pick.trip.stopsToRide} stop{(pick.trip.stopsToRide == 1 ? "" : "s")}.";
        }
        else if (end is not null && rows.Count(r => r.servesDestination == true) == 1)
        {
            var pick = rows.First(r => r.servesDestination == true);
            suitable = pick.busId;
            suitableReason = $"The only one that stops at {to}.";
        }

        return ServiceResult<object>.Ok(new
        {
            buses = rows,
            highlights = new
            {
                shortestRouteBusId = shortest,
                lowestFareBusId = lowestFare,
                mostSuitableBusId = suitable,
                mostSuitableReason = suitableReason,
            },
            notes = new[]
            {
                "Route length is measured along the stops' map positions, which are approximate.",
                "Seats free counts riders who boarded through the app on buses in service now; the app has no seat booking.",
                "Departure times are not in the route data; departure info shows the frequency where one is recorded.",
            },
            unresolved = new { from = from is { Length: > 0 } && start is null ? from : null, to = to is { Length: > 0 } && end is null ? to : null },
        });
    }

    [GeneratedRegex(@"(^|[\s(\-])(non[\s-]?ac)([\s)\-]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex NonAcPattern();

    [GeneratedRegex(@"(^|[\s(\-])ac([\s)\-]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex AcPattern();

    // "AC" or "Non-AC" when the bus name says so; null when the data does not record it.
    public static string? AcFromName(string name) =>
        NonAcPattern().IsMatch(name) ? "Non-AC" : AcPattern().IsMatch(name) ? "AC" : null;

    // ---------------- Fare tracking ----------------

    // Each Dhaka City favourite with its current fare (from the buses table), the fare before the last change and
    // its recorded changes. Favourites not watched yet get their baseline now.
    public async Task<List<object>> PriceTrackingAsync(Guid userId, CancellationToken ct)
    {
        await monitor.WatchAllFavouritesAsync(ct);
        await db.SaveChangesAsync(ct);
        var favourites = await db.FavoriteBuses.Where(f => f.UserId == userId).OrderByDescending(f => f.CreatedAt).AsNoTracking().ToListAsync(ct);
        var busIds = favourites.Select(f => f.BusId).ToList();
        var current = await db.Buses.AsNoTracking().Where(b => busIds.Contains(b.Id))
            .Select(b => new { b.Id, b.BaseFare, b.PerKmFare }).ToDictionaryAsync(b => b.Id, ct);
        var history = (await db.BusFareHistory.AsNoTracking().Where(h => busIds.Contains(h.BusId))
            .OrderByDescending(h => h.RecordedAt).ToListAsync(ct)).ToLookup(h => h.BusId);

        var result = new List<object>();
        foreach (var favourite in favourites)
        {
            var bus = await routes.ByIdAsync(favourite.BusId, ct);
            if (bus is null || !bus.IsDhakaCity || !current.TryGetValue(favourite.BusId, out var fare))
            {
                continue;
            }
            var entries = history[favourite.BusId].ToList();
            var lastChange = entries.FirstOrDefault(h => h.PreviousBaseFare is not null || h.PreviousPerKmFare is not null);
            decimal? previous = lastChange?.PreviousBaseFare;
            result.Add(new
            {
                busId = bus.Id,
                busName = bus.BusName,
                routeNumber = bus.RouteNumber,
                current_base_fare = fare.BaseFare,
                current_per_km_fare = fare.PerKmFare,
                previous_base_fare = previous,
                previous_per_km_fare = lastChange?.PreviousPerKmFare,
                change = previous is { } p ? fare.BaseFare - p : (decimal?)null,
                direction = lastChange is null ? "unchanged"
                    : previous is { } q && fare.BaseFare > q ? "increased"
                    : previous is { } r && fare.BaseFare < r ? "decreased"
                    : "changed",
                lastChangedAt = lastChange?.RecordedAt,
                trackingSince = entries.LastOrDefault()?.RecordedAt,
                changes = entries.Count(h => h.PreviousBaseFare is not null || h.PreviousPerKmFare is not null),
            });
        }
        return result;
    }

    public async Task<ServiceResult<List<FareHistoryEntryViewModel>>> PriceHistoryAsync(Guid userId, Guid busId, CancellationToken ct)
    {
        var check = await guard.FavouriteCityBusAsync(userId, busId, ct);
        if (!check.Succeeded)
        {
            return check.As<List<FareHistoryEntryViewModel>>();
        }
        await monitor.EnsureWatchedAsync(busId, ct);
        await db.SaveChangesAsync(ct);
        var rows = await db.BusFareHistory.AsNoTracking().Where(h => h.BusId == busId).OrderByDescending(h => h.RecordedAt).Take(100).ToListAsync(ct);
        return ServiceResult<List<FareHistoryEntryViewModel>>.Ok(rows.Select(FareHistoryEntryViewModel.From).ToList());
    }

    // ---------------- Recently viewed ----------------

    // Records that the user opened a Dhaka City bus: one row per bus (moved to the top when seen again), and only
    // the most recent ten are kept. Buses outside the city are refused and never stored.
    public async Task<ServiceResult<bool>> RecordViewAsync(Guid userId, Guid busId, CancellationToken ct)
    {
        var check = await guard.CityBusAsync(busId, ct);
        if (!check.Succeeded)
        {
            return check.As<bool>();
        }
        var now = DateTime.UtcNow;
        // Insert or update in one statement, so two tabs viewing the same bus cannot make two rows.
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO recently_viewed_buses (id, user_id, bus_id, viewed_at) VALUES ({Guid.NewGuid()}, {userId}, {busId}, {now})
            ON CONFLICT (user_id, bus_id) DO UPDATE SET viewed_at = EXCLUDED.viewed_at", ct);
        var keep = await db.RecentlyViewedBuses.Where(v => v.UserId == userId).OrderByDescending(v => v.ViewedAt)
            .Take(RecentlyViewedBus.Keep).Select(v => v.Id).ToListAsync(ct);
        await db.RecentlyViewedBuses.Where(v => v.UserId == userId && !keep.Contains(v.Id)).ExecuteDeleteAsync(ct);
        return ServiceResult<bool>.Ok(true);
    }

    public async Task<List<object>> RecentlyViewedAsync(Guid userId, CancellationToken ct)
    {
        var rows = await db.RecentlyViewedBuses.AsNoTracking().Where(v => v.UserId == userId)
            .OrderByDescending(v => v.ViewedAt).Take(RecentlyViewedBus.Keep).ToListAsync(ct);
        var result = new List<object>();
        foreach (var row in rows)
        {
            var bus = await routes.ByIdAsync(row.BusId, ct);
            if (bus is null || !bus.IsDhakaCity)
            {
                continue;
            }
            result.Add(new
            {
                busId = bus.Id,
                busName = bus.BusName,
                routeNumber = bus.RouteNumber,
                @operator = bus.Operator,
                startingStop = bus.Stops.FirstOrDefault()?.Name,
                destination = bus.Stops.LastOrDefault()?.Name,
                route = bus.Stops.Select(s => s.Name).ToList(),
                base_fare = bus.BaseFare,
                viewedAt = row.ViewedAt,
            });
        }
        return result;
    }

    public Task<int> ClearRecentlyViewedAsync(Guid userId, CancellationToken ct) =>
        db.RecentlyViewedBuses.Where(v => v.UserId == userId).ExecuteDeleteAsync(ct);

    // ---------------- Dashboard ----------------

    public async Task<object> SummaryAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var favouriteIds = await db.FavoriteBuses.Where(f => f.UserId == userId).Select(f => f.BusId).ToListAsync(ct);
        var cityFavourites = 0;
        foreach (var id in favouriteIds)
        {
            if ((await routes.ByIdAsync(id, ct))?.IsDhakaCity == true) cityFavourites++;
        }
        return new
        {
            favouriteBuses = cityFavourites,
            outsideCityFavourites = favouriteIds.Count - cityFavourites,
            collections = await db.FavoriteCollections.CountAsync(c => c.UserId == userId, ct),
            upcomingReminders = await db.BusReminders.CountAsync(r => r.UserId == userId && r.IsEnabled && r.SentAt == null && r.DepartureAt > now, ct),
            activeAlerts = await db.BusAvailabilityAlerts.CountAsync(a => a.UserId == userId && a.IsActive, ct),
            unreadNotifications = await ModuleNotifications(userId).CountAsync(n => !n.IsRead, ct),
        };
    }

    // ---------------- Notifications (the module's categories only) ----------------

    private IQueryable<Notification> ModuleNotifications(Guid userId) =>
        db.Notifications.Where(n => n.UserId == userId && NotificationCategories.Bus.Contains(n.Category));

    public async Task<(List<Notification> Items, int Total, int Unread)> NotificationsAsync(Guid userId, int page, int pageSize, bool unreadOnly, string? category, CancellationToken ct)
    {
        var query = ModuleNotifications(userId);
        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(n => n.Category == category);
        }
        if (unreadOnly)
        {
            query = query.Where(n => !n.IsRead);
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).AsNoTracking().ToListAsync(ct);
        var unread = await ModuleNotifications(userId).CountAsync(n => !n.IsRead, ct);
        return (items, total, unread);
    }

    public async Task<bool> MarkNotificationReadAsync(Guid userId, Guid id, CancellationToken ct) =>
        await ModuleNotifications(userId).Where(n => n.Id == id).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct) > 0;

    public Task<int> MarkAllNotificationsReadAsync(Guid userId, CancellationToken ct) =>
        ModuleNotifications(userId).Where(n => !n.IsRead).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);

    public async Task<bool> DeleteNotificationAsync(Guid userId, Guid id, CancellationToken ct) =>
        await ModuleNotifications(userId).Where(n => n.Id == id).ExecuteDeleteAsync(ct) > 0;
}
