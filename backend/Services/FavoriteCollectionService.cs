using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

// A user's collections of favourite Dhaka City buses. Every action is for the signed-in user only: another
// user's collection is reported as not found. Only the user's own favourites, and only Dhaka City buses, can be
// added to a collection.
public class FavoriteCollectionService(AppDbContext db, BusRouteCache routes, DhakaBusGuard guard)
{
    public async Task<List<FavouriteCollectionViewModel>> ListAsync(Guid userId, CancellationToken ct)
    {
        var collections = await db.FavoriteCollections
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Name)
            .AsNoTracking()
            .ToListAsync(ct);
        var result = new List<FavouriteCollectionViewModel>(collections.Count);
        foreach (var collection in collections)
        {
            result.Add(await ToViewModelAsync(collection, ct));
        }
        return result;
    }

    public async Task<ServiceResult<FavouriteCollectionViewModel>> CreateAsync(Guid userId, string? name, CancellationToken ct)
    {
        var clean = Clean(name);
        if (clean.Length == 0)
        {
            return ServiceResult<FavouriteCollectionViewModel>.BadRequest("Give the collection a name.");
        }
        if (await db.FavoriteCollections.CountAsync(c => c.UserId == userId, ct) >= FavoriteCollection.MaxPerUser)
        {
            return ServiceResult<FavouriteCollectionViewModel>.Conflict($"You can have up to {FavoriteCollection.MaxPerUser} collections.");
        }
        if (await NameTakenAsync(userId, clean, null, ct))
        {
            return ServiceResult<FavouriteCollectionViewModel>.Conflict("You already have a collection with this name.");
        }
        var collection = new FavoriteCollection { UserId = userId, Name = clean };
        db.FavoriteCollections.Add(collection);
        if (!await TrySaveAsync(ct))
        {
            return ServiceResult<FavouriteCollectionViewModel>.Conflict("You already have a collection with this name.");
        }
        return ServiceResult<FavouriteCollectionViewModel>.Created(await ToViewModelAsync(collection, ct), "Collection created.");
    }

    public async Task<ServiceResult<FavouriteCollectionViewModel>> RenameAsync(Guid userId, Guid collectionId, string? name, CancellationToken ct)
    {
        var clean = Clean(name);
        if (clean.Length == 0)
        {
            return ServiceResult<FavouriteCollectionViewModel>.BadRequest("Give the collection a name.");
        }
        var collection = await OwnedAsync(userId, collectionId, ct);
        if (collection is null)
        {
            return ServiceResult<FavouriteCollectionViewModel>.NotFound("Collection not found.");
        }
        if (await NameTakenAsync(userId, clean, collectionId, ct))
        {
            return ServiceResult<FavouriteCollectionViewModel>.Conflict("You already have a collection with this name.");
        }
        collection.Name = clean;
        collection.UpdatedAt = DateTime.UtcNow;
        if (!await TrySaveAsync(ct))
        {
            return ServiceResult<FavouriteCollectionViewModel>.Conflict("You already have a collection with this name.");
        }
        return ServiceResult<FavouriteCollectionViewModel>.Ok(await ToViewModelAsync(collection, ct), "Collection renamed.");
    }

    // Deletes the collection only; its buses stay in the user's favourites.
    public async Task<bool> DeleteAsync(Guid userId, Guid collectionId, CancellationToken ct) =>
        await db.FavoriteCollections.Where(c => c.Id == collectionId && c.UserId == userId).ExecuteDeleteAsync(ct) > 0;

    // Adds one of the user's favourite Dhaka City buses to a collection; with fromCollectionId it is moved out of
    // that collection in the same save, so a move never leaves it in both or neither.
    public async Task<ServiceResult<FavouriteCollectionViewModel>> AddBusAsync(Guid userId, Guid collectionId, Guid busId, Guid? fromCollectionId, CancellationToken ct)
    {
        var collection = await OwnedAsync(userId, collectionId, ct);
        if (collection is null)
        {
            return ServiceResult<FavouriteCollectionViewModel>.NotFound("Collection not found.");
        }
        var check = await guard.FavouriteCityBusAsync(userId, busId, ct);
        if (!check.Succeeded)
        {
            return check.As<FavouriteCollectionViewModel>();
        }
        var favorite = await db.FavoriteBuses.FirstAsync(f => f.UserId == userId && f.BusId == busId, ct);

        if (fromCollectionId is { } fromId && fromId != collectionId)
        {
            if (await OwnedAsync(userId, fromId, ct) is null)
            {
                return ServiceResult<FavouriteCollectionViewModel>.NotFound("The collection to move from was not found.");
            }
            // Tracked, not deleted at once: the removal is saved together with the addition below.
            var leaving = await db.FavoriteCollectionBuses.FirstOrDefaultAsync(cb => cb.CollectionId == fromId && cb.FavoriteBusId == favorite.Id, ct);
            if (leaving is not null)
            {
                db.FavoriteCollectionBuses.Remove(leaving);
            }
        }

        var already = await db.FavoriteCollectionBuses.AnyAsync(cb => cb.CollectionId == collectionId && cb.FavoriteBusId == favorite.Id, ct);
        if (!already)
        {
            db.FavoriteCollectionBuses.Add(new FavoriteCollectionBus { CollectionId = collectionId, FavoriteBusId = favorite.Id });
            collection.UpdatedAt = DateTime.UtcNow;
        }
        await TrySaveAsync(ct); // one save for the move; a simultaneous add of the same bus gives the same result
        return ServiceResult<FavouriteCollectionViewModel>.Ok(await ToViewModelAsync(collection, ct),
            already ? "This bus is already in the collection." : fromCollectionId is null ? "Bus added to the collection." : "Bus moved to the collection.");
    }

    public async Task<ServiceResult<FavouriteCollectionViewModel>> RemoveBusAsync(Guid userId, Guid collectionId, Guid busId, CancellationToken ct)
    {
        var collection = await OwnedAsync(userId, collectionId, ct);
        if (collection is null)
        {
            return ServiceResult<FavouriteCollectionViewModel>.NotFound("Collection not found.");
        }
        var removed = await db.FavoriteCollectionBuses
            .Where(cb => cb.CollectionId == collectionId && cb.FavoriteBus!.BusId == busId && cb.FavoriteBus.UserId == userId)
            .ExecuteDeleteAsync(ct);
        if (removed > 0)
        {
            collection.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return ServiceResult<FavouriteCollectionViewModel>.Ok(await ToViewModelAsync(collection, ct),
            removed > 0 ? "Bus removed from the collection." : "This bus was not in the collection.");
    }

    private static string Clean(string? name) => string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private Task<FavoriteCollection?> OwnedAsync(Guid userId, Guid collectionId, CancellationToken ct) =>
        db.FavoriteCollections.FirstOrDefaultAsync(c => c.Id == collectionId && c.UserId == userId, ct);

    // Names are unique per user whatever the letter case ("Office" and "office" are the same collection).
    private Task<bool> NameTakenAsync(Guid userId, string name, Guid? except, CancellationToken ct)
    {
        var lower = name.ToLower();
        return db.FavoriteCollections.AnyAsync(c => c.UserId == userId && c.Name.ToLower() == lower && c.Id != except, ct);
    }

    private async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task<FavouriteCollectionViewModel> ToViewModelAsync(FavoriteCollection collection, CancellationToken ct)
    {
        var favorites = await db.FavoriteCollectionBuses
            .Where(cb => cb.CollectionId == collection.Id)
            .OrderBy(cb => cb.AddedAt)
            .Select(cb => cb.FavoriteBus!)
            .AsNoTracking()
            .ToListAsync(ct);
        var buses = new List<FavoriteBusViewModel>(favorites.Count);
        foreach (var favorite in favorites)
        {
            buses.Add(FavoriteBusViewModel.From(favorite, await routes.ByIdAsync(favorite.BusId, ct)));
        }
        return new FavouriteCollectionViewModel
        {
            Id = collection.Id.ToString(),
            Name = collection.Name,
            BusCount = buses.Count,
            Buses = buses,
            CreatedAt = collection.CreatedAt,
            UpdatedAt = collection.UpdatedAt,
        };
    }
}
