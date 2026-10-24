using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// Account management for admins: find users and reset a forgotten password.
[Authorize(Roles = UserRoles.Admin)]
[Route("admin")]
public class AdminController(AppDbContext db, NotificationService notifications) : ApiControllerBase
{
    private static readonly PasswordHasher<User> Hasher = new();

    // No look-alike characters (0/O, 1/l/I) so the password is easy to read out to the user.
    private const string Letters = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digits = "23456789";

    [HttpGet("users")]
    public async Task<IActionResult> Users([FromQuery] string? search, [FromQuery] int limit = 50)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim().ToLowerInvariant()}%";
            query = query.Where(u => EF.Functions.ILike(u.Name, term) || EF.Functions.ILike(u.Email, term));
        }
        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync();
        return Ok(new { success = true, count = users.Count, data = users.Select(AdminUserViewModel.From) });
    }

    // Sets a new random password and signs the user out everywhere. The password is only shown in this response.
    [HttpPost("users/{userId:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid userId)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null)
        {
            return NotFoundError("User not found.");
        }
        if (user.Role == UserRoles.Admin)
        {
            return BadRequestError("Admin passwords are managed in the server configuration and cannot be reset here.");
        }

        var temporaryPassword = NewTemporaryPassword();
        user.PasswordHash = Hasher.HashPassword(user, temporaryPassword);
        user.UpdatedAt = DateTime.UtcNow;
        notifications.Notify(user.Id, "Password reset",
            "An admin reset your password. Please choose a new password from your profile.", NotificationTypes.Warning);
        await db.SaveChangesAsync();

        await db.UserSessions
            .Where(s => s.UserId == user.Id && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow));

        return Ok(new
        {
            success = true,
            message = $"Password reset for {user.Email}.",
            temporaryPassword,
            user = AdminUserViewModel.From(user),
        });
    }

    // 10 characters with at least one letter and one digit, so it passes the normal password rules.
    private static string NewTemporaryPassword()
    {
        var chars = (RandomNumberGenerator.GetString(Letters, 7) + RandomNumberGenerator.GetString(Digits, 3)).ToCharArray();
        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }
}
