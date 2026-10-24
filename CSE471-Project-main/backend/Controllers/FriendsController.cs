using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// Older friend endpoints used by the Flutter FriendsService. Adding by code here connects both
// users straight away; the request/accept flow lives in AuthController.
[Authorize]
[Route("api/friends")]
public class FriendsController(AppDbContext db, NotificationService notifications) : ApiControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> List()
    {
        var friends = await FriendsQuery.ForUser(db, CurrentUserId);
        return Ok(new { success = true, data = friends.Select(UserViewModel.From) });
    }

    [HttpPost("add")]
    public async Task<IActionResult> Add(AddFriendByCodeRequest request)
    {
        var me = await db.Users.FindAsync(CurrentUserId);
        var code = request.FriendCode.Trim().ToUpperInvariant();
        var friend = await db.Users.FirstOrDefaultAsync(u => u.FriendCode == code);
        if (me is null || friend is null)
        {
            return NotFoundError("No user found with that friend code.");
        }
        if (friend.Id == me.Id)
        {
            return BadRequestError("You cannot add yourself.");
        }
        if (await db.Friendships.AnyAsync(f => f.UserId == me.Id && f.FriendId == friend.Id))
        {
            return BadRequestError("You are already friends.");
        }

        db.Friendships.Add(new Friendship { UserId = me.Id, FriendId = friend.Id });
        db.Friendships.Add(new Friendship { UserId = friend.Id, FriendId = me.Id });
        notifications.Notify(friend.Id, "New friend", $"{me.Name} added you as a friend.", NotificationTypes.Success);
        await db.SaveChangesAsync();
        return Ok(new { success = true, message = "Friend added.", data = UserViewModel.From(friend) });
    }
}
