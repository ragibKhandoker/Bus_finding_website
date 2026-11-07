using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

// A user's favourite buses. Adding and removing are idempotent, so a repeated click or a retried request gives
// the same result instead of an error. The (user_id, bus_id) unique index keeps one row per bus.
public class FavoriteBusService(AppDbContext db, BusRouteCache routes)
{
    // Created: false when the bus was already a favourite (the existing one is returned).
    public async Task<ServiceResult<(FavoriteBusViewModel Favorite, bool Created)>> AddAsync(Guid userId, Guid busId, CancellationToken ct)
    {
        var bus = await db.Buses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == busId, ct);
        if (bus is null)
        {
            return ServiceResult<(FavoriteBusViewModel, bool)>.NotFound("Bus not found.");
        }

        var existing = await db.FavoriteBuses.AsNoTracking().FirstOrDefaultAsync(f => f.UserId == userId && f.BusId == busId, ct);
        if (existing is not null)
        {
            return Already(await ToViewModelAsync(existing, ct));
        }

        var favorite = new FavoriteBus
        {
            UserId = userId,
            BusId = bus.Id,
            BusName = bus.BusName,
            RouteNumber = bus.RouteNumber,
            Operator = bus.Operator,
        };
        db.FavoriteBuses.Add(favorite);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A simultaneous request saved the same favourite first; the result is the same.
            db.ChangeTracker.Clear();
            existing = await db.FavoriteBuses.AsNoTracking().FirstAsync(f => f.UserId == userId && f.BusId == busId, ct);
            return Already(await ToViewModelAsync(existing, ct));
        }
        return ServiceResult<(FavoriteBusViewModel, bool)>.Created((await ToViewModelAsync(favorite, ct), true), "Bus added to favourites.");
    }

    // Removes only the user's favourite row, never the bus itself. True if there was one.
    public async Task<bool> RemoveAsync(Guid userId, Guid busId, CancellationToken ct) =>
        await db.FavoriteBuses.Where(f => f.UserId == userId && f.BusId == busId).ExecuteDeleteAsync(ct) > 0;

    // The user's favourites, newest first, with current bus details.
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

    private static ServiceResult<(FavoriteBusViewModel, bool)> Already(FavoriteBusViewModel favorite) =>
        ServiceResult<(FavoriteBusViewModel, bool)>.Ok((favorite, false), "This bus is already in your favourites.");

    private async Task<FavoriteBusViewModel> ToViewModelAsync(FavoriteBus favorite, CancellationToken ct) =>
        FavoriteBusViewModel.From(favorite, await routes.ByIdAsync(favorite.BusId, ct));
}
