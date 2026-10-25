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
    public Task<Trip?> ActiveTripAsync(Guid userId) =>
        db.Trips.Include(t => t.IndividualBus).FirstOrDefaultAsync(t => t.UserId == userId && t.Status == TripStatus.Active);

    public async Task<TripResult> BoardAsync(Guid userId, Guid vehicleId, string? boardingStop, string? destinationStop)
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
        };
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

    // source is only needed when the trip was started without a boarding stop.
    public async Task<TripResult> EndAsync(Guid userId, Guid vehicleId, string? destinationStop, string? source = null)
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
        var target = string.IsNullOrWhiteSpace(destinationStop) ? trip.Destination : destinationStop;
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
