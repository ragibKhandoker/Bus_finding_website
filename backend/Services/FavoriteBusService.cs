using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

// A user's favourite buses (Dhaka City buses only). Adding and removing are idempotent, so a repeated click or a
// retried request gives the same result instead of an error. The (user_id, bus_id) unique index keeps one row per
// bus. Adding starts watching the bus for fare and route changes (FavouriteMonitor).
public class FavoriteBusService(AppDbContext db, BusRouteCache routes, DhakaBusGuard guard, FavouriteMonitor monitor, NotificationService notifications)
{
    // Created: false when the bus was already a favourite (the existing one is returned).
    public async Task<ServiceResult<(FavoriteBusViewModel Favorite, bool Created)>> AddAsync(Guid userId, Guid busId, CancellationToken ct)
    {
        var check = await guard.CityBusAsync(busId, ct);
        if (!check.Succeeded)
        {
            return check.As<(FavoriteBusViewModel, bool)>();
        }
        var bus = check.Value!;

        // Two tries: a simultaneous request may save the same favourite, or start watching the same bus, first.
        for (var attempt = 0; ; attempt++)
        {
            var existing = await db.FavoriteBuses.AsNoTracking().FirstOrDefaultAsync(f => f.UserId == userId && f.BusId == busId, ct);
            if (existing is not null)
            {
                return Already(await ToViewModelAsync(existing, ct));
            }

            var favorite = new FavoriteBus
            {
                UserId = userId,
                BusId = busId,
                BusName = bus.BusName,
                RouteNumber = bus.RouteNumber,
                Operator = bus.Operator,
            };
            db.FavoriteBuses.Add(favorite);
            await monitor.EnsureWatchedAsync(busId, ct);
            notifications.Notify(userId, "Favourite added", $"{Label(bus)} is now one of your favourite Dhaka buses.",
                NotificationTypes.Success, "/favourites", NotificationCategories.BusFavourite, relatedId: busId);
            try
            {
                await db.SaveChangesAsync(ct);
                return ServiceResult<(FavoriteBusViewModel, bool)>.Created((await ToViewModelAsync(favorite, ct), true), "Bus added to favourites.");
            }
            catch (DbUpdateException ex) when (attempt == 0 && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    // Removes only the user's favourite row, never the bus itself (it also leaves the user's collections). True if
    // there was one.
    public async Task<bool> RemoveAsync(Guid userId, Guid busId, CancellationToken ct)
    {
        var removed = await db.FavoriteBuses.Where(f => f.UserId == userId && f.BusId == busId).ExecuteDeleteAsync(ct) > 0;
        if (removed)
        {
            var bus = await routes.ByIdAsync(busId, ct);
            notifications.Notify(userId, "Favourite removed", $"{(bus is null ? "The bus" : Label(bus))} was removed from your favourite Dhaka buses.",
                NotificationTypes.Info, "/favourites", NotificationCategories.BusFavourite, relatedId: busId);
            await db.SaveChangesAsync(ct);
        }
        return removed;
    }

    // The user's favourites, newest first, with current bus details. Favourites saved before the Dhaka-only rule
    // that run outside the city are still listed (isDhakaCity false), so the user can see and remove them.
    public async Task<List<FavoriteBusViewModel>> ForUserAsync(Guid userId, CancellationToken ct)
    {
        var favorites = await db.FavoriteBuses
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .AsNoTracking()
            .ToListAsync(ct);
        var data = new List<FavoriteBusViewModel>(favorites.Count);
        foreach (var favorite in favorites)
        {
            data.Add(await ToViewModelAsync(favorite, ct));
        }
        return data;
    }

    public Task<bool> IsFavoriteAsync(Guid userId, Guid busId, CancellationToken ct) =>
        db.FavoriteBuses.AnyAsync(f => f.UserId == userId && f.BusId == busId, ct);

    public static string Label(BusViewModel bus) => bus.RouteNumber is { Length: > 0 } number ? $"{bus.BusName} ({number})" : bus.BusName;

    private static ServiceResult<(FavoriteBusViewModel, bool)> Already(FavoriteBusViewModel favorite) =>
        ServiceResult<(FavoriteBusViewModel, bool)>.Ok((favorite, false), "This bus is already in your favourites.");

    private async Task<FavoriteBusViewModel> ToViewModelAsync(FavoriteBus favorite, CancellationToken ct) =>
        FavoriteBusViewModel.From(favorite, await routes.ByIdAsync(favorite.BusId, ct));
}
