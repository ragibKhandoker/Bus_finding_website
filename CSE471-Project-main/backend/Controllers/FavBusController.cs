using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

[Authorize]
[Route("api/favbus")]
public class FavBusController(AppDbContext db) : ApiControllerBase
{
    [HttpPost("add")]
    public async Task<IActionResult> Add(FavoriteBusRequest request)
    {
        var userId = CurrentUserId;
        var bus = await db.Buses.FindAsync(request.BusId);
        if (bus is null)
        {
            return NotFoundError("Bus not found.");
        }
        if (await db.FavoriteBuses.AnyAsync(f => f.UserId == userId && f.BusId == bus.Id))
        {
            return BadRequestError("This bus is already in your favorites.");
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
        await db.SaveChangesAsync();
        return Created201(new { success = true, message = "Bus added to favorites.", data = FavoriteBusViewModel.From(favorite) });
    }

    [HttpDelete("remove")]
    public async Task<IActionResult> Remove(FavoriteBusRequest request)
    {
        var favorite = await db.FavoriteBuses.FirstOrDefaultAsync(f => f.UserId == CurrentUserId && f.BusId == request.BusId);
        if (favorite is null)
        {
            return NotFoundError("This bus is not in your favorites.");
        }
        db.FavoriteBuses.Remove(favorite);
        await db.SaveChangesAsync();
        return Ok(new { success = true, message = "Bus removed from favorites." });
    }

    [HttpGet("user/{userId:guid}")]
    public async Task<IActionResult> ForUser(Guid userId)
    {
        if (userId != CurrentUserId && !IsAdmin)
        {
            return ForbiddenError();
        }
        var favorites = await db.FavoriteBuses
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .AsNoTracking()
            .ToListAsync();
        return Ok(new { success = true, count = favorites.Count, data = favorites.Select(FavoriteBusViewModel.From) });
    }

    [HttpGet("check")]
    public async Task<IActionResult> Check([FromQuery] Guid busId)
    {
        var isFavorited = await db.FavoriteBuses.AnyAsync(f => f.UserId == CurrentUserId && f.BusId == busId);
        return Ok(new { success = true, isFavorited });
    }
}
