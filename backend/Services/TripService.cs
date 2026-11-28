using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

// The outcome of a board or end-trip request: Status is the HTTP status to answer with, Message the text for the
// rider, and Trip/Wallet are set on success.
public record TripResult(int Status, string Message, Trip? Trip = null, Wallet? Wallet = null)
{
    public bool Ok => Status is >= 200 and < 300;
}

// How a trip was started and where the phone was, when the rider shared it.
public record BoardingDetails(string Method = BoardingMethods.Manual, double? Lat = null, double? Lng = null);

// Boarding a bus and ending the trip. A rider has at most one active trip (also enforced by a unique index).
// Ending a trip settles everything in one SaveChanges, which is one database transaction: the trip is completed,
// the seat freed, the fare saved and the gems credited, or none of it happens.
//
// Fare rule: the route's per-km rate (Bus.PerKmFare) times the distance ridden along the route's stops, rounded
// up to whole taka, and never below the route's minimum fare (Bus.BaseFare). Distance is measured stop to stop in
// straight lines, so it is an estimate.
// Gems rule: every completed trip earns Rewards:GemsPerTrip gems (appsettings.json), once per trip.
public class TripService(AppDbContext db, BusRouteCache routes, WalletService wallets)
{
    // Only this rider's own trips, newest first.
    public Task<List<Trip>> HistoryAsync(Guid userId) => db.Trips
        .Include(t => t.IndividualBus)
        .Where(t => t.UserId == userId)
        .OrderByDescending(t => t.CreatedAt)
        .AsNoTracking()
        .ToListAsync();

    // Logs a finished trip straight into history (no boarding, no gems). Created: false when the same trip was
    // just sent (a double click or a retry) and the earlier one is returned instead.
    public async Task<ServiceResult<(Trip Trip, bool Created)>> LogAsync(Guid userId, AddTripRequest request)
    {
        var busId = request.BusId.Trim();
        var source = request.Source?.Trim() ?? "";
        var destination = request.Destination?.Trim() ?? "";
        if (busId.Length == 0)
        {
            return ServiceResult<(Trip, bool)>.BadRequest("busId is required.");
        }
        if (source.Length == 0 || destination.Length == 0)
        {
            return ServiceResult<(Trip, bool)>.BadRequest("Enter where the trip started and ended.");
        }
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<(Trip, bool)>.BadRequest("The start and end of a trip cannot be the same place.");
        }
        if (await db.Trips.AnyAsync(t => t.UserId == userId && t.Status == TripStatus.Active && t.BusId == busId))
        {
            return ServiceResult<(Trip, bool)>.Conflict("You are on this bus right now. End the trip instead of logging it.");
        }
        // The same trip sent twice in a row is saved once.
        var since = DateTime.UtcNow.AddMinutes(-2);
        var duplicate = await db.Trips.FirstOrDefaultAsync(t => t.UserId == userId && t.BusId == busId && t.Source == source &&
                                                              t.Destination == destination && t.CreatedAt > since);
        if (duplicate is not null)
        {
            return ServiceResult<(Trip, bool)>.Ok((duplicate, false), "Trip already added.");
        }

        var trip = new Trip
        {
            UserId = userId,
            BusId = busId,
            BusName = string.IsNullOrWhiteSpace(request.BusName) ? "Bus" : request.BusName.Trim(),
            Distance = request.Distance,
            Fare = request.Fare,
            Source = source,
            Destination = destination,
            Status = TripStatus.Completed,
            EndedAt = DateTime.UtcNow,
        };
        // A bus picked from the routes (busId is a route id) with stops on that route: the server works out the
        // distance, fare and stops from the route, as for a boarded trip, and keeps the route's details. Anything
        // else (a bus id typed in by hand, or stops that are not on the route) is logged as sent.
        var route = Guid.TryParse(busId, out var routeId) ? await routes.ByIdAsync(routeId) : null;
        if (route is not null)
        {
            trip.BusName = route.BusName;
            Snapshot(trip, route, null);
            var from = StopIndex(route, source);
            var to = StopIndex(route, destination);
            if (from >= 0 && CheckRide(route, from, to, destination) is null)
            {
                var km = DistanceKm(route, from, to);
                trip.Source = route.Stops[from].Name;
                trip.Destination = route.Stops[to].Name;
                trip.Distance = Math.Round(km, 2);
                trip.Fare = Fare(route, km);
                trip.StopsRidden = StopsBetween(route, from, to);
            }
        }
        db.Trips.Add(trip);
        await db.SaveChangesAsync();
        return ServiceResult<(Trip, bool)>.Created((trip, true), "Trip added.");
    }

    public Task<Trip?> ActiveTripAsync(Guid userId) =>
        db.Trips.Include(t => t.IndividualBus).FirstOrDefaultAsync(t => t.UserId == userId && t.Status == TripStatus.Active);

    // A stop counts as "where you are" when the phone is within this distance of it.
    public const double NearStopKm = 1.0;

    public async Task<TripResult> BoardAsync(Guid userId, Guid vehicleId, string? boardingStop, string? destinationStop, BoardingDetails? details = null)
    {
        var vehicle = await db.IndividualBuses.FirstOrDefaultAsync(b => b.Id == vehicleId);
        if (vehicle is null)
        {
            return new TripResult(StatusCodes.Status404NotFound, "Bus not found.");
        }
        var route = await routes.ByIdAsync(vehicle.BusId);
        if (route is null)
        {
            return new TripResult(StatusCodes.Status404NotFound, "This bus has no route.");
        }

        var active = await ActiveTripAsync(userId);
        if (active is not null)
        {
            return active.IndividualBusId == vehicle.Id
                ? new TripResult(StatusCodes.Status200OK, "You are already on this bus.", active)
                : new TripResult(StatusCodes.Status409Conflict, $"You already have an active trip on {active.BusName}. End it before boarding another bus.");
        }

        if (string.IsNullOrWhiteSpace(boardingStop))
        {
            return new TripResult(StatusCodes.Status400BadRequest, "Choose the stop where you are getting on.");
        }
        var from = StopIndex(route, boardingStop);
        if (from < 0)
        {
            return new TripResult(StatusCodes.Status400BadRequest, $"{route.BusName} does not stop at \"{boardingStop.Trim()}\".");
        }
        string destination = "";
        if (!string.IsNullOrWhiteSpace(destinationStop))
        {
            var to = StopIndex(route, destinationStop);
            var problem = CheckRide(route, from, to, destinationStop);
            if (problem is not null)
            {
                return new TripResult(StatusCodes.Status400BadRequest, problem);
            }
            destination = route.Stops[to].Name;
        }

        if (vehicle.CurrentPassengerCount >= vehicle.TotalPassengerCapacity)
        {
            return new TripResult(StatusCodes.Status400BadRequest, "This bus is full. Pick another bus on the route.");
        }

        var trip = new Trip
        {
            UserId = userId,
            IndividualBusId = vehicle.Id,
            BusId = route.Id,
            BusName = route.BusName,
            Source = route.Stops[from].Name,
            Destination = destination,
            Status = TripStatus.Active,
            BoardingMethod = details?.Method ?? BoardingMethods.Manual,
            BoardingLat = ValidLat(details?.Lat, details?.Lng),
            BoardingLng = ValidLat(details?.Lat, details?.Lng) is null ? null : details?.Lng,
        };
        Snapshot(trip, route, vehicle);
        db.Trips.Add(trip);
        db.BusBoardings.Add(new BusBoarding { UserId = userId, IndividualBusId = vehicle.Id });
        vehicle.CurrentPassengerCount++;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // A second board request from the same rider arrived at the same moment and won.
            db.ChangeTracker.Clear();
            return new TripResult(StatusCodes.Status409Conflict, "You already have an active trip. End it before boarding another bus.");
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return new TripResult(StatusCodes.Status409Conflict, "The bus seats changed while you were boarding. Please try again.");
        }
        trip.IndividualBus = vehicle;
        return new TripResult(StatusCodes.Status201Created, $"You boarded {route.BusName} at {trip.Source}.", trip);
    }

    // Ends the rider's active trip, whichever bus it is on (POST /trip/end). With no stop named, the stop nearest
    // to the phone's location is used, if one on the route is close enough.
    public async Task<TripResult> EndActiveAsync(Guid userId, string? destinationStop, double? lat, double? lng)
    {
        var active = await db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.UserId == userId && t.Status == TripStatus.Active);
        if (active?.IndividualBusId is not { } vehicleId)
        {
            return new TripResult(StatusCodes.Status400BadRequest, "You do not have an active trip.");
        }
        return await EndAsync(userId, vehicleId, destinationStop, null, lat, lng);
    }

    // source is only needed when the trip was started without a boarding stop. lat/lng: where the phone is, used to
    // pick the stop when none is named.
    public async Task<TripResult> EndAsync(Guid userId, Guid vehicleId, string? destinationStop, string? source = null, double? lat = null, double? lng = null)
    {
        var trip = await db.Trips.Include(t => t.IndividualBus)
            .FirstOrDefaultAsync(t => t.UserId == userId && t.IndividualBusId == vehicleId && t.Status == TripStatus.Active);
        if (trip is null)
        {
            var finished = await db.Trips.AnyAsync(t => t.UserId == userId && t.IndividualBusId == vehicleId && t.Status == TripStatus.Completed);
            return finished
                ? new TripResult(StatusCodes.Status409Conflict, "This trip is already completed.")
                : new TripResult(StatusCodes.Status400BadRequest, "You do not have an active trip on this bus.");
        }

        var route = Guid.TryParse(trip.BusId, out var routeId) ? await routes.ByIdAsync(routeId) : null;
        if (route is null)
        {
            return new TripResult(StatusCodes.Status404NotFound, "The route of this trip no longer exists.");
        }
        var from = StopIndex(route, string.IsNullOrWhiteSpace(trip.Source) ? source ?? "" : trip.Source);
        if (from < 0)
        {
            return new TripResult(StatusCodes.Status400BadRequest, "Choose the stop where you got on.");
        }
        var hasLocation = ValidLat(lat, lng) is not null;
        var target = string.IsNullOrWhiteSpace(destinationStop) ? null : destinationStop;
        if (target is null && hasLocation)
        {
            var near = NearestStop(route, lat!.Value, lng!.Value, i => CheckRide(route, from, i, route.Stops[i].Name) is null);
            if (near is not { Km: <= NearStopKm })
            {
                return new TripResult(StatusCodes.Status400BadRequest, "No stop on this route is near you. Choose the stop where you got off.");
            }
            target = route.Stops[near.Value.Index].Name;
        }
        target ??= trip.Destination;
        if (string.IsNullOrWhiteSpace(target))
        {
            return new TripResult(StatusCodes.Status400BadRequest, "Choose the stop where you got off.");
        }
        var to = StopIndex(route, target);
        var problem = CheckRide(route, from, to, target);
        if (problem is not null)
        {
            return new TripResult(StatusCodes.Status400BadRequest, problem);
        }

        var km = DistanceKm(route, from, to);
        var gems = wallets.Rewards.GemsPerTrip;
        trip.Source = route.Stops[from].Name;
        trip.Destination = route.Stops[to].Name;
        trip.Distance = Math.Round(km, 2);
        trip.Fare = Fare(route, km);
        trip.Status = TripStatus.Completed;
        trip.EndedAt = DateTime.UtcNow;
        trip.GemsEarned = gems;
        if (hasLocation)
        {
            trip.EndLat = lat;
            trip.EndLng = lng;
        }
        Snapshot(trip, route, trip.IndividualBus);
        trip.StopsRidden = StopsBetween(route, from, to);

        var boarding = await db.BusBoardings.FirstOrDefaultAsync(b => b.UserId == userId && b.IndividualBusId == vehicleId && b.EndedAt == null);
        if (boarding is not null)
        {
            boarding.EndedAt = trip.EndedAt;
        }
        if (trip.IndividualBus is { } vehicle)
        {
            vehicle.CurrentPassengerCount = Math.Max(0, vehicle.CurrentPassengerCount - 1);
        }
        var wallet = await wallets.GetOrCreateAsync(userId);
        wallets.AddGems(wallet, gems, $"Trip on {trip.BusName}: {trip.Source} to {trip.Destination}", trip.Id);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || ex is DbUpdateException u && IsUniqueViolation(u))
        {
            // Another request completed this trip (or changed the wallet) first; nothing from this one was saved.
            db.ChangeTracker.Clear();
            var completed = await db.Trips.AnyAsync(t => t.Id == trip.Id && t.Status == TripStatus.Completed);
            return completed
                ? new TripResult(StatusCodes.Status409Conflict, "This trip is already completed.")
                : new TripResult(StatusCodes.Status409Conflict, "Your wallet changed at the same moment. Please try ending the trip again.");
        }
        return new TripResult(StatusCodes.Status200OK, $"Trip completed! Fare {trip.Fare:0} Tk. You earned {gems} gems.", trip, wallet);
    }

    // Copies the route's and the vehicle's details onto the trip, so its history does not depend on them later.
    private static void Snapshot(Trip trip, BusViewModel route, IndividualBus? vehicle)
    {
        trip.RouteNumber = route.RouteNumber;
        trip.BusType = string.IsNullOrWhiteSpace(route.BusType) ? null : route.BusType;
        trip.Operator = route.Operator;
        trip.BaseFare = route.BaseFare;
        trip.PerKmFare = route.PerKmFare;
        if (vehicle is not null)
        {
            trip.BusCode = vehicle.BusCode;
        }
    }

    // The route's stop closest to a point (among those allowed), with its distance in km; null if none is allowed.
    public static (int Index, double Km)? NearestStop(BusViewModel route, double lat, double lng, Func<int, bool>? allowed = null)
    {
        (int Index, double Km)? best = null;
        for (var i = 0; i < route.Stops.Count; i++)
        {
            if (allowed is not null && !allowed(i))
            {
                continue;
            }
            var km = GeoService.HaversineKm(lat, lng, route.Stops[i].Lat, route.Stops[i].Lng);
            if (best is null || km < best.Value.Km)
            {
                best = (i, km);
            }
        }
        return best;
    }

    // The latitude when lat/lng are a real point (not missing, not 0,0), else null.
    public static double? ValidLat(double? lat, double? lng) =>
        lat is >= -90 and <= 90 && lng is >= -180 and <= 180 && !(lat == 0 && lng == 0) ? lat : null;

    // The stops from one index to another inclusive, in the order ridden (backwards on a return journey).
    public static List<string> StopsBetween(BusViewModel route, int from, int to) => from <= to
        ? route.Stops.Skip(from).Take(to - from + 1).Select(s => s.Name).ToList()
        : route.Stops.Skip(to).Take(from - to + 1).Select(s => s.Name).Reverse().ToList();

    // Position of a stop on the route by name (case and spacing ignored), or -1.
    public static int StopIndex(BusViewModel route, string name)
    {
        var key = BusRouteMatcher.Normalize(name);
        return route.Stops.FindIndex(s => BusRouteMatcher.Normalize(s.Name) == key);
    }

    private static string? CheckRide(BusViewModel route, int from, int to, string destination)
    {
        if (to < 0)
        {
            return $"{route.BusName} does not stop at \"{destination.Trim()}\".";
        }
        if (to == from)
        {
            return "Your destination is the stop where you got on.";
        }
        if (to < from && !route.RunsBothWays)
        {
            return $"{route.BusName} only runs one way; {route.Stops[to].Name} comes before {route.Stops[from].Name}.";
        }
        return null;
    }

    // Distance along the route between two stops, in either direction.
    public static double DistanceKm(BusViewModel route, int from, int to)
    {
        var (start, end) = from < to ? (from, to) : (to, from);
        double km = 0;
        for (var i = start; i < end; i++)
        {
            km += GeoService.HaversineKm(route.Stops[i].Lat, route.Stops[i].Lng, route.Stops[i + 1].Lat, route.Stops[i + 1].Lng);
        }
        return km;
    }

    public static decimal Fare(BusViewModel route, double km) =>
        Math.Max(route.BaseFare, Math.Ceiling(route.PerKmFare * (decimal)km));

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
