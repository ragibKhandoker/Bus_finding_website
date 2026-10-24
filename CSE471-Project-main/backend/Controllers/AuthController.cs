using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

[Route("auth")]
public class AuthController(
    AppDbContext db,
    JwtTokenService tokens,
    FriendCodeGenerator friendCodes,
    NotificationService notifications) : ApiControllerBase
{
    private static readonly PasswordHasher<User> Hasher = new();

    [EnableRateLimiting("strict")]
    [HttpPost("signup")]
    public async Task<IActionResult> Signup(SignupRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email))
        {
            return BadRequestError("An account with this email already exists.");
        }

        var role = (request.Role ?? UserRoles.Commuter).Trim().ToLowerInvariant();
        if (!UserRoles.SelfAssignable.Contains(role))
        {
            return BadRequestError("Role must be commuter or driver.");
        }

        var user = new User
        {
            Name = request.Name.Trim(),
            Email = email,
            Gender = string.IsNullOrWhiteSpace(request.Gender) ? "Prefer not to say" : request.Gender.Trim(),
            Role = role,
            FriendCode = await friendCodes.GenerateUniqueAsync(),
        };
        user.PasswordHash = Hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        db.Wallets.Add(new Wallet { User = user });
        await db.SaveChangesAsync();

        return Ok(new { success = true, message = "Account created successfully.", user = UserViewModel.From(user) });
    }

    [EnableRateLimiting("strict")]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
        {
            return Error(StatusCodes.Status401Unauthorized, "Invalid email or password.");
        }

        var result = Hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            return Error(StatusCodes.Status401Unauthorized, "Invalid email or password.");
        }
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = Hasher.HashPassword(user, request.Password);
            await db.SaveChangesAsync();
        }

        return Ok(new { success = true, token = tokens.CreateToken(user), user = UserViewModel.From(user) });
    }

    [Authorize]
    [HttpGet("validate")]
    public async Task<IActionResult> Validate()
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user is null)
        {
            return Error(StatusCodes.Status401Unauthorized, "Account no longer exists.");
        }
        return Ok(new { success = true, valid = true, user = UserViewModel.From(user) });
    }

    [Authorize]
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(UpdateProfileRequest request)
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user is null)
        {
            return NotFoundError("User not found.");
        }

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var email = request.Email.Trim().ToLowerInvariant();
            if (email != user.Email && await db.Users.AnyAsync(u => u.Email == email))
            {
                return BadRequestError("That email is already used by another account.");
            }
            user.Email = email;
        }
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            user.Name = request.Name.Trim();
        }
        if (!string.IsNullOrWhiteSpace(request.Gender))
        {
            user.Gender = request.Gender.Trim();
        }
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { success = true, message = "Profile updated.", user = UserViewModel.From(user) });
    }

    [Authorize]
    [HttpPut("password")]
    public async Task<IActionResult> UpdatePassword(UpdatePasswordRequest request)
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user is null)
        {
            return NotFoundError("User not found.");
        }
        if (Hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            return BadRequestError("Current password is incorrect.");
        }

        user.PasswordHash = Hasher.HashPassword(user, request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { success = true, message = "Password updated." });
    }

    [Authorize]
    [HttpGet("friend-code")]
    public async Task<IActionResult> GetFriendCode()
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user is null)
        {
            return NotFoundError("User not found.");
        }
        return Ok(new { success = true, friendCode = user.FriendCode });
    }

    // Returns the user object itself (not wrapped) because the Flutter client parses the body as a User.
    [Authorize]
    [HttpGet("search-friend/{friendCode}")]
    public async Task<IActionResult> SearchFriend(string friendCode)
    {
        var code = friendCode.Trim().ToUpperInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.FriendCode == code);
        if (user is null)
        {
            return NotFoundError("No user found with that friend code.");
        }
        if (user.Id == CurrentUserId)
        {
            return BadRequestError("That is your own friend code.");
        }
        return Ok(UserViewModel.From(user));
    }

    [Authorize]
    [HttpPost("send-friend-request")]
    public async Task<IActionResult> SendFriendRequest(SendFriendRequestRequest request)
    {
        var me = CurrentUserId;
        var toUserId = request.ToUserId!.Value;
        if (toUserId == me)
        {
            return BadRequestError("You cannot add yourself.");
        }

        var target = await db.Users.FindAsync(toUserId);
        var sender = await db.Users.FindAsync(me);
        if (target is null || sender is null)
        {
            return NotFoundError("User not found.");
        }
        if (await db.Friendships.AnyAsync(f => f.UserId == me && f.FriendId == toUserId))
        {
            return BadRequestError("You are already friends.");
        }

        var pending = await db.FriendRequests.FirstOrDefaultAsync(r =>
            r.Status == FriendRequestStatus.Pending &&
            ((r.FromUserId == me && r.ToUserId == toUserId) || (r.FromUserId == toUserId && r.ToUserId == me)));
        if (pending is not null)
        {
            return BadRequestError(pending.FromUserId == me
                ? "Friend request already sent."
                : "This person already sent you a request. Accept it from your pending requests.");
        }

        db.FriendRequests.Add(new FriendRequest { FromUserId = me, ToUserId = toUserId });
        notifications.Notify(toUserId, "New friend request", $"{sender.Name} wants to add you as a friend.");
        await db.SaveChangesAsync();
        return Ok(new { success = true, message = "Friend request sent." });
    }

    [Authorize]
    [HttpGet("pending-friend-requests")]
    public async Task<IActionResult> PendingFriendRequests()
    {
        var requests = await db.FriendRequests
            .Include(r => r.FromUser)
            .Where(r => r.ToUserId == CurrentUserId && r.Status == FriendRequestStatus.Pending)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
        return Ok(new { success = true, requests = requests.Select(FriendRequestViewModel.From) });
    }

    [Authorize]
    [HttpPut("accept-friend-request/{requestId:guid}")]
    public async Task<IActionResult> AcceptFriendRequest(Guid requestId)
    {
        var request = await db.FriendRequests.Include(r => r.ToUser).FirstOrDefaultAsync(r => r.Id == requestId);
        if (request is null || request.ToUserId != CurrentUserId)
        {
            return NotFoundError("Friend request not found.");
        }
        if (request.Status != FriendRequestStatus.Pending)
        {
            return BadRequestError("This request was already handled.");
        }

        request.Status = FriendRequestStatus.Accepted;
        if (!await db.Friendships.AnyAsync(f => f.UserId == request.FromUserId && f.FriendId == request.ToUserId))
        {
            db.Friendships.Add(new Friendship { UserId = request.FromUserId, FriendId = request.ToUserId });
            db.Friendships.Add(new Friendship { UserId = request.ToUserId, FriendId = request.FromUserId });
        }
        notifications.Notify(request.FromUserId, "Friend request accepted",
            $"{request.ToUser!.Name} accepted your friend request.", NotificationTypes.Success);
        await db.SaveChangesAsync();
        return Ok(new { success = true, message = "Friend request accepted." });
    }

    [Authorize]
    [HttpPut("reject-friend-request/{requestId:guid}")]
    public async Task<IActionResult> RejectFriendRequest(Guid requestId)
    {
        var request = await db.FriendRequests.FindAsync(requestId);
        if (request is null || request.ToUserId != CurrentUserId)
        {
            return NotFoundError("Friend request not found.");
        }
        if (request.Status != FriendRequestStatus.Pending)
        {
            return BadRequestError("This request was already handled.");
        }

        request.Status = FriendRequestStatus.Rejected;
        await db.SaveChangesAsync();
        return Ok(new { success = true, message = "Friend request declined." });
    }

    [Authorize]
    [HttpGet("friends")]
    public async Task<IActionResult> Friends()
    {
        var friends = await FriendsQuery.ForUser(db, CurrentUserId);
        return Ok(new { success = true, friends = friends.Select(UserViewModel.From) });
    }
}

internal static class FriendsQuery
{
    public static Task<List<User>> ForUser(AppDbContext db, Guid userId) =>
        db.Friendships
            .Where(f => f.UserId == userId)
            .Select(f => f.Friend!)
            .OrderBy(u => u.Name)
            .ToListAsync();
}
