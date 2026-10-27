using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
    FriendService friends,
    NotificationService notifications) : ApiControllerBase
{
    private static readonly PasswordHasher<User> Hasher = new();

    [EnableRateLimiting("strict")]
    [HttpPost("signup")]
    public async Task<IActionResult> Signup(SignupRequest request)
    {
        var name = request.Name.Trim();
        if (name.Length < 2)
        {
            return BadRequestError("Name must be between 2 and 100 characters.");
        }
        if (request.Password.Trim().Length == 0)
        {
            return BadRequestError("Password cannot be only spaces.");
        }

        var role = string.IsNullOrWhiteSpace(request.Role) ? UserRoles.Commuter : request.Role.Trim().ToLowerInvariant();
        if (!UserRoles.SelfAssignable.Contains(role))
        {
            return BadRequestError("Role must be commuter or driver.");
        }
        if (UserGenders.Normalize(request.Gender) is not { } gender)
        {
            return BadRequestError($"Gender must be one of: {string.Join(", ", UserGenders.All)}.");
        }

        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email))
        {
            return Error(StatusCodes.Status409Conflict, "An account with this email already exists.");
        }

        var user = new User
        {
            Name = name,
            Email = email,
            Gender = gender,
            Role = role,
            FriendCode = await friendCodes.GenerateUniqueAsync(),
        };
        user.PasswordHash = Hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        db.Wallets.Add(new Wallet { User = user });
        // The user, wallet and friend code are saved together. If a simultaneous sign-up took the same friend
        // code first, the unique index rejects the insert and it is retried with a new code.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync();
                break;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
                                               && pg.ConstraintName?.Contains("email") == true)
            {
                // Another sign-up with the same email won the race between the check above and the insert.
                return Error(StatusCodes.Status409Conflict, "An account with this email already exists.");
            }
            catch (DbUpdateException ex) when (FriendCodeGenerator.IsCodeClash(ex) && attempt < 5)
            {
                user.FriendCode = await friendCodes.GenerateUniqueAsync();
            }
        }

        // Sign the new user straight in so the client does not need a second login call.
        var session = await StartSessionAsync(user);
        return Ok(new
        {
            success = true,
            message = "Account created successfully.",
            token = tokens.CreateToken(user, session),
            expiresAt = session.ExpiresAt,
            user = UserViewModel.From(user),
        });
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
        }

        var session = await StartSessionAsync(user);
        return Ok(new
        {
            success = true,
            token = tokens.CreateToken(user, session),
            expiresAt = session.ExpiresAt,
            user = UserViewModel.From(user),
        });
    }

    // Ends the session behind the current token; the token is rejected from then on.
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (CurrentSessionId is { } sessionId)
        {
            await db.UserSessions
                .Where(s => s.Id == sessionId && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow));
        }
        return Ok(new { success = true, message = "Signed out." });
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
        var expiresAt = await db.UserSessions
            .Where(s => s.Id == CurrentSessionId)
            .Select(s => (DateTime?)s.ExpiresAt)
            .FirstOrDefaultAsync();
        return Ok(new { success = true, valid = true, expiresAt, user = UserViewModel.From(user) });
    }

    private Guid? CurrentSessionId => Guid.TryParse(User.FindFirstValue("jti"), out var id) ? id : null;

    // Blank optional profile text is stored as null rather than an empty string.
    private static string? OptionalText(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Records a new sign-in and clears out this user's sessions that can no longer be used.
    private async Task<UserSession> StartSessionAsync(User user)
    {
        var now = DateTime.UtcNow;
        await db.UserSessions
            .Where(s => s.UserId == user.Id && (s.ExpiresAt <= now || s.RevokedAt != null))
            .ExecuteDeleteAsync();

        var userAgent = Request.Headers.UserAgent.ToString();
        var session = new UserSession
        {
            UserId = user.Id,
            CreatedAt = now,
            ExpiresAt = tokens.NewExpiry(),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = userAgent.Length > 300 ? userAgent[..300] : userAgent,
        };
        db.UserSessions.Add(session);
        user.LastLoginAt = now;
        await db.SaveChangesAsync();
        return session;
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
                return Error(StatusCodes.Status409Conflict, "That email is already used by another account.");
            }
            user.Email = email;
        }
        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length < 2)
            {
                return BadRequestError("Name must be between 2 and 100 characters.");
            }
            user.Name = name;
        }
        if (!string.IsNullOrWhiteSpace(request.Gender))
        {
            if (UserGenders.Normalize(request.Gender) is not { } gender)
            {
                return BadRequestError($"Gender must be one of: {string.Join(", ", UserGenders.All)}.");
            }
            user.Gender = gender;
        }
        if (request.Phone is not null)
        {
            user.Phone = OptionalText(request.Phone);
        }
        if (request.Bio is not null)
        {
            user.Bio = OptionalText(request.Bio);
        }
        if (request.HomeArea is not null)
        {
            user.HomeArea = OptionalText(request.HomeArea);
        }
        if (request.WorkArea is not null)
        {
            user.WorkArea = OptionalText(request.WorkArea);
        }
        user.UpdatedAt = DateTime.UtcNow;
        notifications.Notify(user.Id, "Profile updated", "Your profile has been updated.", NotificationTypes.Success,
            category: NotificationCategories.Account);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
                                           && pg.ConstraintName?.Contains("email") == true)
        {
            // Another account took the same email between the check above and the update.
            return Error(StatusCodes.Status409Conflict, "That email is already used by another account.");
        }

        return Ok(new { success = true, message = "Your profile has been updated.", user = UserViewModel.From(user) });
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

    // The code is created once at sign-up and never changes; this only reads it.
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

    // Looks a person up by their exact friend code: ?code=R7K2M. Returns their public profile and how the caller
    // is already connected to them ("none", "self", "friends", "request_sent" or "request_received").
    [Authorize]
    [EnableRateLimiting("friend-search")]
    [HttpGet("search-friend")]
    public async Task<IActionResult> SearchFriendByQuery([FromQuery] string? code)
    {
        if (FriendCodeGenerator.Normalize(code) is not { } normalized)
        {
            return BadRequestError("A friend code is exactly 5 letters or digits.");
        }
        var user = await friends.FindByCodeAsync(normalized);
        if (user is null)
        {
            return NotFoundError("User not found.");
        }
        var (relationship, requestId) = await friends.RelationshipAsync(CurrentUserId, user.Id);
        return Ok(new { success = true, user = FriendProfileViewModel.From(user), relationship, requestId });
    }

    // ---- Older friend endpoints used by the Flutter client. They share FriendService with /friends. ----

    // Returns the profile itself (not wrapped) because the Flutter client parses the body as a User.
    [Authorize]
    [EnableRateLimiting("friend-search")]
    [HttpGet("search-friend/{friendCode}")]
    public async Task<IActionResult> SearchFriend(string friendCode)
    {
        if (FriendCodeGenerator.Normalize(friendCode) is not { } code)
        {
            return BadRequestError("A friend code is exactly 5 letters or digits.");
        }
        var user = await friends.FindByCodeAsync(code);
        if (user is null)
        {
            return NotFoundError("No user found with that friend code.");
        }
        if (user.Id == CurrentUserId)
        {
            return BadRequestError("That is your own friend code.");
        }
        return Ok(FriendProfileViewModel.From(user));
    }

    [Authorize]
    [HttpPost("send-friend-request")]
    public async Task<IActionResult> SendFriendRequest(SendFriendRequestRequest request) =>
        LegacyResult(await friends.SendRequestAsync(CurrentUserId, request.ToUserId!.Value));

    [Authorize]
    [HttpGet("pending-friend-requests")]
    public async Task<IActionResult> PendingFriendRequests()
    {
        var requests = await friends.Incoming(CurrentUserId).ToListAsync();
        return Ok(new { success = true, requests = requests.Select(FriendRequestViewModel.From) });
    }

    [Authorize]
    [HttpPut("accept-friend-request/{requestId:guid}")]
    public async Task<IActionResult> AcceptFriendRequest(Guid requestId) =>
        LegacyResult(await friends.AcceptAsync(CurrentUserId, requestId));

    [Authorize]
    [HttpPut("reject-friend-request/{requestId:guid}")]
    public async Task<IActionResult> RejectFriendRequest(Guid requestId) =>
        LegacyResult(await friends.RejectAsync(CurrentUserId, requestId));

    [Authorize]
    [HttpGet("friends")]
    public async Task<IActionResult> Friends()
    {
        var list = await friends.FriendsOfAsync(CurrentUserId);
        return Ok(new { success = true, friends = list.Select(UserViewModel.From) });
    }

    private IActionResult LegacyResult(FriendResult result) => result.Succeeded
        ? Ok(new { success = true, message = result.Message })
        : Error(result.StatusCode, result.Message);
}
