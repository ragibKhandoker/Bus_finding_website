using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// Sign-up, sign-in, the signed-in user's account, forgotten passwords and the older friend endpoints.
// The rules live in AccountService, PasswordResetService and FriendService; this only maps them to HTTP.
[Route("auth")]
public class AuthController(
    AccountService accounts,
    FriendService friends,
    PasswordResetService passwordResets) : ApiControllerBase
{
    [EnableRateLimiting("strict")]
    [HttpPost("signup")]
    public async Task<IActionResult> Signup(SignupRequest request)
    {
        var result = await accounts.SignupAsync(request, Client);
        return result.Succeeded ? SignedInResponse(result.Value!, result.Message) : Error(result.StatusCode, result.Message);
    }

    [EnableRateLimiting("strict")]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var result = await accounts.LoginAsync(request.Email, request.Password, Client, request.Role);
        return result.Succeeded ? SignedInResponse(result.Value!) : Error(result.StatusCode, result.Message);
    }

    // Ends the session behind the current token; the token is rejected from then on.
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (CurrentSessionId is { } sessionId)
        {
            await accounts.LogoutAsync(sessionId);
        }
        return Ok(new { success = true, message = "Signed out." });
    }

    [Authorize]
    [HttpGet("validate")]
    public async Task<IActionResult> Validate()
    {
        if (await accounts.ValidateAsync(CurrentUserId, CurrentSessionId) is not { } found)
        {
            return Error(StatusCodes.Status401Unauthorized, "Account no longer exists.");
        }
        return Ok(new { success = true, valid = true, expiresAt = found.ExpiresAt, user = UserViewModel.From(found.User) });
    }

    [Authorize]
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(UpdateProfileRequest request)
    {
        var result = await accounts.UpdateProfileAsync(CurrentUserId, request);
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, user = UserViewModel.From(result.Value!) })
            : Error(result.StatusCode, result.Message);
    }

    [Authorize]
    [HttpPut("password")]
    public async Task<IActionResult> UpdatePassword(UpdatePasswordRequest request)
    {
        var result = await accounts.UpdatePasswordAsync(CurrentUserId, request.CurrentPassword, request.NewPassword);
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, user = UserViewModel.From(result.Value!) })
            : Error(result.StatusCode, result.Message);
    }

    // ---- Forgot password: ask an admin for a temporary password (no sign-in needed) ----

    // Returns the request id and a secret. The browser keeps both; the secret is the only way to see the
    // temporary password once an admin sends it.
    [EnableRateLimiting("strict")]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        var result = await passwordResets.CreateAsync(request.Email, request.Message);
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        var (created, secret) = result.Value;
        return Created201(new
        {
            success = true,
            message = result.Message,
            requestId = created.Id,
            secret,
            request = ResetStatusViewModel.From(created, DateTime.UtcNow),
        });
    }

    // The requester checks whether an admin has answered. POST, so the secret is not written to URL logs.
    [EnableRateLimiting("strict")]
    [HttpPost("forgot-password/status")]
    public async Task<IActionResult> ForgotPasswordStatus(ResetStatusRequest request)
    {
        var found = await passwordResets.FindWithSecretAsync(request.RequestId, request.Secret);
        return found is null
            ? NotFoundError("Request not found. Please send a new request.")
            : Ok(new { success = true, request = ResetStatusViewModel.From(found, DateTime.UtcNow) });
    }

    // The person who asked collects a temporary password for an approved request, proving the request's secret.
    // It is made now, stored only as a bcrypt hash, and appears in this response only: never in the database,
    // never to an admin. Asking again makes a new one and the previous one stops working.
    [EnableRateLimiting("strict")]
    [HttpPost("forgot-password/reveal")]
    public async Task<IActionResult> RevealTemporaryPassword(ResetStatusRequest request)
    {
        var result = await passwordResets.RevealAsync(request.RequestId, request.Secret);
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        var (password, expiresAt) = result.Value;
        // Never cache a response that contains a password.
        Response.Headers.CacheControl = "no-store";
        return Ok(new { success = true, message = result.Message, temporaryPassword = password, expiresAt });
    }

    // The code is created once at sign-up and never changes; this only reads it.
    [Authorize]
    [HttpGet("friend-code")]
    public async Task<IActionResult> GetFriendCode() => await accounts.FriendCodeAsync(CurrentUserId) is { } code
        ? Ok(new { success = true, friendCode = code })
        : NotFoundError("User not found.");

    private Guid? CurrentSessionId => Guid.TryParse(User.FindFirstValue("jti"), out var id) ? id : null;

    private ClientInfo Client => new(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());

    private OkObjectResult SignedInResponse(SignedIn signedIn, string message = "") => message.Length > 0
        ? Ok(new { success = true, message, token = signedIn.Token, expiresAt = signedIn.ExpiresAt, user = UserViewModel.From(signedIn.User) })
        : Ok(new { success = true, token = signedIn.Token, expiresAt = signedIn.ExpiresAt, user = UserViewModel.From(signedIn.User) });

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
