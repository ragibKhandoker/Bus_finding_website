using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// A signed-in user's favourite buses. Every action works on the caller's own favourites, taken from the token;
// a user id sent in the body is ignored. The rules live in FavoriteBusService.
[Authorize]
[Route("api/favbus")]
public class FavBusController(FavoriteBusService favorites) : ApiControllerBase
{
    [HttpPost("add")]
    public async Task<IActionResult> Add(FavoriteBusRequest request, CancellationToken ct)
    {
        var result = await favorites.AddAsync(CurrentUserId, request.BusGuid, ct);
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        var (favorite, created) = result.Value;
        var body = new { success = true, message = result.Message, isFavourite = true, created, data = favorite };
        return created ? Created201(body) : Ok(body);
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
        var removed = await favorites.RemoveAsync(CurrentUserId, busId, ct);
        return Ok(new
        {
            success = true,
            message = removed ? "Bus removed from favourites." : "This bus was not in your favourites.",
            isFavourite = false,
            removed,
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
        var data = await favorites.ForUserAsync(userId, ct);
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
        var isFavourite = await favorites.IsFavoriteAsync(CurrentUserId, id, ct);
        return Ok(new { success = true, busId = id, isFavourite, isFavorited = isFavourite });
    }
}
