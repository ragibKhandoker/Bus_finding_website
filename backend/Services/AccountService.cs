using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

// Who is signing in, recorded on the session.
public record ClientInfo(string? IpAddress, string UserAgent);

// A signed-in session: the user and the token their client sends from now on.
public record SignedIn(User User, string Token, DateTime ExpiresAt);

// Accounts: sign-up, sign-in and sessions, profile and password changes. AuthController calls this.
public class AccountService(
    AppDbContext db,
    JwtTokenService tokens,
    FriendCodeGenerator friendCodes,
    OfferService offers,
    NotificationService notifications,
    PasswordResetService passwordResets)
{

    public async Task<ServiceResult<SignedIn>> SignupAsync(SignupRequest request, ClientInfo client)
    {
        var name = request.Name.Trim();
        if (name.Length < 2)
        {
            return ServiceResult<SignedIn>.BadRequest("Name must be between 2 and 100 characters.");
        }
        if (PasswordHashing.IsTooLong(request.Password))
        {
            return ServiceResult<SignedIn>.BadRequest("Password is too long. Use at most 72 characters.");
        }
        if (request.Password.Trim().Length == 0)
        {
            return ServiceResult<SignedIn>.BadRequest("Password cannot be only spaces.");
        }

        var role = string.IsNullOrWhiteSpace(request.Role) ? UserRoles.Commuter : request.Role.Trim().ToLowerInvariant();
        if (!UserRoles.SelfAssignable.Contains(role))
        {
            return ServiceResult<SignedIn>.BadRequest("Role must be commuter or driver.");
        }
        if (UserGenders.Normalize(request.Gender) is not { } gender)
        {
            return ServiceResult<SignedIn>.BadRequest($"Gender must be one of: {string.Join(", ", UserGenders.All)}.");
        }

        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email))
        {
            return ServiceResult<SignedIn>.Conflict("An account with this email already exists.");
        }

        var user = new User
        {
            Name = name,
            Email = email,
            Gender = gender,
            Role = role,
            FriendCode = await friendCodes.GenerateUniqueAsync(),
        };
        user.PasswordHash = PasswordHashing.Hash(request.Password);
        db.Users.Add(user);
        var wallet = new Wallet { User = user };
        db.Wallets.Add(wallet);
        // The welcome offer from the Rewards settings is saved in the same transaction as the account.
        offers.AddWelcomeOffers(user, wallet);
        // The user, wallet, offers and friend code are saved together. If a simultaneous sign-up took the same friend
        // code first, the unique index rejects the insert and it is retried with a new code.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync();
                break;
            }
            catch (DbUpdateException ex) when (IsEmailClash(ex))
            {
                // Another sign-up with the same email won the race between the check above and the insert.
                return ServiceResult<SignedIn>.Conflict("An account with this email already exists.");
            }
            catch (DbUpdateException ex) when (FriendCodeGenerator.IsCodeClash(ex) && attempt < 5)
            {
                user.FriendCode = await friendCodes.GenerateUniqueAsync();
            }
        }

        // Sign the new user straight in so the client does not need a second login call.
        return ServiceResult<SignedIn>.Ok(await StartSessionAsync(user, client), "Account created successfully.");
    }

    // role: the role chosen on the sign-in form. The account must have exactly that role, or no session is made.
    public async Task<ServiceResult<SignedIn>> LoginAsync(string email, string password, ClientInfo client, string? role = null)
    {
        email = email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
        {
            return ServiceResult<SignedIn>.Unauthorized("Invalid email or password.");
        }

        var result = PasswordHashing.Verify(user, password);
        if (result == PasswordCheck.Failed)
        {
            return ServiceResult<SignedIn>.Unauthorized("Invalid email or password.");
        }
        // Each account signs in only through the role it was registered with. Checked after the password, so the
        // role of an account is only told to someone who knows its password.
        if (!string.IsNullOrWhiteSpace(role) && !string.Equals(role.Trim(), user.Role, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<SignedIn>.Forbidden(WrongRoleMessage(user.Role));
        }
        // A temporary password from an admin only works for a limited time.
        if (user.MustChangePassword && user.TemporaryPasswordExpiresAt is { } expires && expires <= DateTime.UtcNow)
        {
            return ServiceResult<SignedIn>.Unauthorized(
                "This temporary password has expired. Choose Forgot password? to ask the admin for a new one.");
        }
        // An account from before bcrypt: store a bcrypt hash now that the password is known.
        if (result == PasswordCheck.OkNeedsRehash)
        {
            user.PasswordHash = PasswordHashing.Hash(password);
        }
        return ServiceResult<SignedIn>.Ok(await StartSessionAsync(user, client));
    }

    // Ends one session; its token is rejected from then on.
    public Task<int> LogoutAsync(Guid sessionId) => db.UserSessions
        .Where(s => s.Id == sessionId && s.RevokedAt == null)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow));

    // The signed-in user and when their session ends, or null if the account is gone.
    public async Task<(User User, DateTime? ExpiresAt)?> ValidateAsync(Guid userId, Guid? sessionId)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null)
        {
            return null;
        }
        var expiresAt = await db.UserSessions
            .Where(s => s.Id == sessionId)
            .Select(s => (DateTime?)s.ExpiresAt)
            .FirstOrDefaultAsync();
        return (user, expiresAt);
    }

    public async Task<string?> FriendCodeAsync(Guid userId) => (await db.Users.FindAsync(userId))?.FriendCode;

    public async Task<ServiceResult<User>> UpdateProfileAsync(Guid userId, UpdateProfileRequest request)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null)
        {
            return ServiceResult<User>.NotFound("User not found.");
        }

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var email = request.Email.Trim().ToLowerInvariant();
            if (email != user.Email && await db.Users.AnyAsync(u => u.Email == email))
            {
                return ServiceResult<User>.Conflict("That email is already used by another account.");
            }
            user.Email = email;
        }
        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (name.Length < 2)
            {
                return ServiceResult<User>.BadRequest("Name must be between 2 and 100 characters.");
            }
            user.Name = name;
        }
        if (!string.IsNullOrWhiteSpace(request.Gender))
        {
            if (UserGenders.Normalize(request.Gender) is not { } gender)
            {
                return ServiceResult<User>.BadRequest($"Gender must be one of: {string.Join(", ", UserGenders.All)}.");
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
        catch (DbUpdateException ex) when (IsEmailClash(ex))
        {
            // Another account took the same email between the check above and the update.
            return ServiceResult<User>.Conflict("That email is already used by another account.");
        }
        return ServiceResult<User>.Ok(user, "Your profile has been updated.");
    }

    public async Task<ServiceResult<User>> UpdatePasswordAsync(Guid userId, string currentPassword, string newPassword)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null)
        {
            return ServiceResult<User>.NotFound("User not found.");
        }
        if (PasswordHashing.Verify(user, currentPassword) == PasswordCheck.Failed)
        {
            return ServiceResult<User>.BadRequest("Current password is incorrect.");
        }
        if (PasswordHashing.IsTooLong(newPassword))
        {
            return ServiceResult<User>.BadRequest("Password is too long. Use at most 72 characters.");
        }

        var replacingTemporary = user.MustChangePassword;
        if (replacingTemporary)
        {
            // The new password must be a real one, not the temporary password again.
            if (newPassword == currentPassword)
            {
                return ServiceResult<User>.BadRequest("Choose a new password that is different from the temporary one.");
            }
            if (!newPassword.Any(char.IsLetter) || !newPassword.Any(char.IsDigit))
            {
                return ServiceResult<User>.BadRequest("Password must contain at least one letter and one number.");
            }
        }

        user.PasswordHash = PasswordHashing.Hash(newPassword);
        user.MustChangePassword = false;
        user.TemporaryPasswordExpiresAt = null;
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        if (replacingTemporary)
        {
            await passwordResets.CompleteAsync(user.Id);
        }
        return ServiceResult<User>.Ok(user, "Password updated.");
    }

    // Records a new sign-in and clears out this user's sessions that can no longer be used.
    private async Task<SignedIn> StartSessionAsync(User user, ClientInfo client)
    {
        var now = DateTime.UtcNow;
        await db.UserSessions
            .Where(s => s.UserId == user.Id && (s.ExpiresAt <= now || s.RevokedAt != null))
            .ExecuteDeleteAsync();

        var session = new UserSession
        {
            UserId = user.Id,
            CreatedAt = now,
            ExpiresAt = tokens.NewExpiry(),
            IpAddress = client.IpAddress,
            UserAgent = client.UserAgent.Length > 300 ? client.UserAgent[..300] : client.UserAgent,
        };
        db.UserSessions.Add(session);
        user.LastLoginAt = now;
        await db.SaveChangesAsync();
        return new SignedIn(user, tokens.CreateToken(user, session), session.ExpiresAt);
    }

    // Tells the person which button their account belongs to.
    public static string WrongRoleMessage(string accountRole) => accountRole switch
    {
        UserRoles.Admin => "This is an admin account. Choose Admin to sign in.",
        UserRoles.Driver => "This is a driver account. Choose Driver to sign in.",
        _ => "This is a commuter account. Choose Commuter to sign in.",
    };

    // Blank optional profile text is stored as null rather than an empty string.
    private static string? OptionalText(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsEmailClash(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg && pg.ConstraintName?.Contains("email") == true;
}
