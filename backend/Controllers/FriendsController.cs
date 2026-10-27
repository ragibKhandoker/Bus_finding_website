using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// Friend requests and the friends list for the web client. The sender, recipient and owner of every list is
// always the signed-in user from the token; no user id is accepted from the client.
[Authorize]
[Route("friends")]
public class FriendsController(FriendService friends) : ApiControllerBase
{
    // The caller's friends by name: ?page=1&pageSize=50 (pageSize at most 100).
    [HttpGet("")]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var me = CurrentUserId;
        var query = friends.Friendships(me);
        return await Paged(query, page, pageSize, f => FriendViewModel.From(FriendService.Friend(f, me), f.CreatedAt));
    }

    [HttpPost("request")]
    public async Task<IActionResult> SendRequest(SendFriendRequestByCodeRequest request)
    {
        var code = FriendCodeGenerator.Normalize(request.FriendCode);
        if (code is null)
        {
            return BadRequestError("A friend code is exactly 5 letters or digits.");
        }
        var receiver = await friends.FindByCodeAsync(code);
        if (receiver is null)
        {
            return NotFoundError("User not found.");
        }
        var result = await friends.SendRequestAsync(CurrentUserId, receiver.Id);
        return Respond(result);
    }

    [HttpGet("requests/incoming")]
    public Task<IActionResult> Incoming([FromQuery] int page = 1, [FromQuery] int pageSize = 50) =>
        Paged(friends.Incoming(CurrentUserId), page, pageSize, FriendRequestDetailsViewModel.From);

    // Requests the caller sent that are still waiting for an answer.
    [HttpGet("requests/sent")]
    public Task<IActionResult> Sent([FromQuery] int page = 1, [FromQuery] int pageSize = 50) =>
        Paged(friends.Sent(CurrentUserId), page, pageSize, FriendRequestDetailsViewModel.From);

    [HttpPost("requests/{requestId:guid}/accept")]
    public async Task<IActionResult> Accept(Guid requestId) => Respond(await friends.AcceptAsync(CurrentUserId, requestId));

    [HttpPost("requests/{requestId:guid}/reject")]
    public async Task<IActionResult> Reject(Guid requestId) => Respond(await friends.RejectAsync(CurrentUserId, requestId));

    // ---- Older endpoints used by the Flutter FriendsService ----

    [HttpGet("/api/friends")]
    public async Task<IActionResult> LegacyList()
    {
        var list = await friends.FriendsOfAsync(CurrentUserId);
        return Ok(new { success = true, data = list.Select(UserViewModel.From) });
    }

    // Used to add the friend straight away; it now sends a friend request that the other person must accept.
    [HttpPost("/api/friends/add")]
    public Task<IActionResult> LegacyAdd(AddFriendByCodeRequest request) =>
        SendRequest(new SendFriendRequestByCodeRequest { FriendCode = request.FriendCode });

    private IActionResult Respond(FriendResult result)
    {
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        var body = new { success = true, message = result.Message, data = FriendRequestDetailsViewModel.From(result.Request!) };
        return result.StatusCode == StatusCodes.Status201Created ? Created201(body) : Ok(body);
    }

    private async Task<IActionResult> Paged<T>(IQueryable<T> query, int page, int pageSize, Func<T, object> map)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new
        {
            success = true,
            page,
            pageSize,
            total,
            totalPages = (int)Math.Ceiling(total / (double)pageSize),
            data = items.Select(map),
        });
    }
}
