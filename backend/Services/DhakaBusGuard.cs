using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

// The server-side gate of the Favourite Bus module: every action that names a bus goes through here, so a bus
// outside Dhaka City (or one that does not exist) is refused whatever the client sends.
public class DhakaBusGuard(AppDbContext db, BusRouteCache routes)
{
    // The bus, when it exists and runs only inside Dhaka City. 404 for an unknown bus, 422 for one outside the city.
    public async Task<ServiceResult<BusViewModel>> CityBusAsync(Guid busId, CancellationToken ct)
    {
        if (busId == Guid.Empty)
        {
            return ServiceResult<BusViewModel>.BadRequest("busId is required.");
        }
        var bus = await routes.ByIdAsync(busId, ct);
        if (bus is null)
        {
            return ServiceResult<BusViewModel>.NotFound("Bus not found.");
        }
        return bus.IsDhakaCity ? ServiceResult<BusViewModel>.Ok(bus) : ServiceResult<BusViewModel>.Unprocessable(DhakaCityArea.OutsideMessage);
    }

    // As CityBusAsync, and the bus must also be one of the user's favourites (collections, comparison, fare
    // tracking, alerts and reminders all work on favourites).
    public async Task<ServiceResult<BusViewModel>> FavouriteCityBusAsync(Guid userId, Guid busId, CancellationToken ct)
    {
        var result = await CityBusAsync(busId, ct);
        if (!result.Succeeded)
        {
            return result;
        }
        return await db.FavoriteBuses.AnyAsync(f => f.UserId == userId && f.BusId == busId, ct)
            ? result
            : ServiceResult<BusViewModel>.Conflict("Add this bus to your favourites first.");
    }

    // Finds a stop on the bus's route by name (spacing and case do not matter). Null when the bus does not stop there.
    public static BusStopViewModel? StopOn(BusViewModel bus, string? name)
    {
        var wanted = BusRouteMatcher.Normalize(name ?? "");
        return wanted.Length == 0 ? null : bus.Stops.FirstOrDefault(s => BusRouteMatcher.Normalize(s.Name) == wanted);
    }
}
