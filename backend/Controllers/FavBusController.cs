using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// A signed-in user's favourite buses. Every action works on the caller's own favourites, taken from the token;
// a user id sent in the body is ignored. Adding and removing are idempotent, so a repeated click or a retried
// request gives the same result instead of an error. The (user_id, bus_id) unique index keeps one row per bus.
[Authorize]
[Route("api/favbus")]
public class FavBusController(AppDbContext db, BusRouteCache routes) : ApiControllerBase
{
    [HttpPost("add")]
    public async Task<IActionResult> Add(FavoriteBusRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        var busId = request.BusGuid;
        var bus = await db.Buses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == busId, ct);
        if (bus is null)
        {
            return NotFoundError("Bus not found.");
        }

        var existing = await db.FavoriteBuses.AsNoTracking().FirstOrDefaultAsync(f => f.UserId == userId && f.BusId == busId, ct);
        if (existing is not null)
        {
            return Ok(new { success = true, message = "This bus is already in your favourites.", isFavourite = true, created = false, data = await ToViewModelAsync(existing, ct) });
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
            return Ok(new { success = true, message = "This bus is already in your favourites.", isFavourite = true, created = false, data = await ToViewModelAsync(existing, ct) });
        }
        return Created201(new { success = true, message = "Bus added to favourites.", isFavourite = true, created = true, data = await ToViewModelAsync(favorite, ct) });
    }

    // DELETE /api/favbus/{busId}. Only the caller's favourite row is removed, never the bus itself.
    [HttpDelete("{busId:guid}")]
    public Task<IActionResult> RemoveById(Guid busId, CancellationToken ct) => RemoveAsync(busId, ct);

    // Older clients send the bus id in the body.
    [HttpDelete("remove")]
    public Task<IActionResult> Remove(FavoriteBusRequest request, CancellationToken ct) => RemoveAsync(request.BusGuid, ct);

    private async Task<IActionResult> RemoveAsync(Guid busId, CancellationToken ct)
    {
        if (busId == Guid.Empty)
        {
            return BadRequestError("busId is required.");
        }
        var removed = await db.FavoriteBuses.Where(f => f.UserId == CurrentUserId && f.BusId == busId).ExecuteDeleteAsync(ct);
        return Ok(new
        {
            success = true,
            message = removed > 0 ? "Bus removed from favourites." : "This bus was not in your favourites.",
            isFavourite = false,
            removed = removed > 0,
        });
    }

    // A user's favourites, newest first, with current bus details. Only the user themselves (or an admin).
    [HttpGet("user/{userId:guid}")]
    public async Task<IActionResult> ForUser(Guid userId, CancellationToken ct)
    {
        if (userId != CurrentUserId && !IsAdmin)
        {
            return ForbiddenError("You can only see your own favourite buses.");
        }
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
        return Ok(new
        {
            success = true,
            count = data.Count,
            message = data.Count == 0 ? "No favourite buses yet." : null,
            data,
        });
    }

    // GET /api/favbus/check?busId=... : whether the bus is one of the caller's favourites.
    // isFavorited is the older name of the same flag, kept for existing clients.
    [HttpGet("check")]
    public async Task<IActionResult> Check([FromQuery] Guid? busId, CancellationToken ct)
    {
        if (busId is not { } id || id == Guid.Empty)
        {
            return BadRequestError("busId is required.");
        }
        var isFavourite = await db.FavoriteBuses.AnyAsync(f => f.UserId == CurrentUserId && f.BusId == id, ct);
        return Ok(new { success = true, busId = id, isFavourite, isFavorited = isFavourite });
    }

    private async Task<FavoriteBusViewModel> ToViewModelAsync(FavoriteBus favorite, CancellationToken ct) =>
        FavoriteBusViewModel.From(favorite, await routes.ByIdAsync(favorite.BusId, ct));
}
