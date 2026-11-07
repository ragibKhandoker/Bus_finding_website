using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

public record SupportResult<T>(int StatusCode, string Message, T? Value = default)
{
    public bool Succeeded => StatusCode is >= 200 and < 300;
}

// Temporary passwords for people who forgot theirs. Only a bcrypt hash is ever stored; the password itself is
// shown once, to the person who asked, and to nobody else (not to admins either).
public static class TemporaryPasswords
{
    // How long an approved request (and the temporary password from it) stays usable.
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    // No look-alike characters (0/O, 1/l/I) so the password is easy to read and type.
    private const string Letters = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digits = "23456789";

    // 10 characters with at least one letter and one digit, so it passes the normal password rules.
    public static string New()
    {
        var chars = (RandomNumberGenerator.GetString(Letters, 7) + RandomNumberGenerator.GetString(Digits, 3)).ToCharArray();
        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }

    // Gives the user a new temporary password (stored as a bcrypt hash) that they must replace after signing in.
    // The caller saves the change, signs the user out everywhere (RevokeSessionsAsync) and hands the returned
    // password only to the user.
    public static string Issue(User user, DateTime now, DateTime expiresAt)
    {
        var password = New();
        user.PasswordHash = PasswordHashing.Hash(password);
        user.MustChangePassword = true;
        user.TemporaryPasswordExpiresAt = expiresAt;
        user.UpdatedAt = now;
        return password;
    }

    public static Task<int> RevokeSessionsAsync(AppDbContext db, Guid userId) => db.UserSessions
        .Where(s => s.UserId == userId && s.RevokedAt == null)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow));
}

// "Forgot password" requests. A signed-out user asks (their browser keeps a secret for the request); an admin
// approves it without ever seeing a password; then the user, proving the secret, gets a temporary password.
// That password is made at that moment, stored only as a bcrypt hash, and returned to the user once.
public class PasswordResetService(AppDbContext db, NotificationService notifications)
{
    public static string HashSecret(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    public async Task<SupportResult<(PasswordResetRequest Request, string Secret)>> CreateAsync(string email, string? note)
    {
        email = email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
        {
            return new(StatusCodes.Status404NotFound, "No account uses that email address. Check it, or sign up for a new account.");
        }
        if (user.Role == UserRoles.Admin)
        {
            return new(StatusCodes.Status400BadRequest, "Admin passwords are set in the server configuration (appsettings.Local.json), not reset by request.");
        }

        var now = DateTime.UtcNow;
        // A newer request replaces one that is still waiting, so the admin sees only the latest.
        await db.PasswordResetRequests
            .Where(r => r.UserId == user.Id && r.Status == PasswordResetStatuses.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, PasswordResetStatuses.Dismissed)
                .SetProperty(r => r.AdminNote, "Replaced by a newer request.")
                .SetProperty(r => r.HandledAt, now));

        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var request = new PasswordResetRequest
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            SecretHash = HashSecret(secret),
            CreatedAt = now,
        };
        db.PasswordResetRequests.Add(request);
        foreach (var adminId in await AdminIdsAsync())
        {
            notifications.Notify(adminId, "Password reset request",
                $"{user.Name} ({user.Email}) forgot their password and asked for a temporary one.",
                NotificationTypes.Warning, actionUrl: "/admin", category: NotificationCategories.Account,
                senderId: user.Id, relatedId: request.Id);
        }
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two requests for the same account at the same moment: the other one is already waiting.
            return new(StatusCodes.Status409Conflict, "A request for this account is already waiting for an admin.");
        }
        request.User = user;
        return new(StatusCodes.Status201Created, "Your request has been sent to the admin.", (request, secret));
    }

    // The request, if the secret matches. Anything else is reported as not found, so ids reveal nothing.
    public async Task<PasswordResetRequest?> FindWithSecretAsync(Guid requestId, string secret)
    {
        var request = await db.PasswordResetRequests.Include(r => r.User).FirstOrDefaultAsync(r => r.Id == requestId);
        if (request is null || string.IsNullOrEmpty(secret))
        {
            return null;
        }
        var expected = Encoding.ASCII.GetBytes(request.SecretHash);
        var actual = Encoding.ASCII.GetBytes(HashSecret(secret));
        return CryptographicOperations.FixedTimeEquals(expected, actual) ? request : null;
    }

    public Task<List<PasswordResetRequest>> ListAsync(string? status)
    {
        var query = db.PasswordResetRequests.Include(r => r.User).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            query = query.Where(r => r.Status == status);
        }
        return query.OrderByDescending(r => r.CreatedAt).Take(100).ToListAsync();
    }

    public Task<int> PendingCountAsync() => db.PasswordResetRequests.CountAsync(r => r.Status == PasswordResetStatuses.Pending);

    // An admin approves a waiting request. No password is made or shown here: the user collects their temporary
    // password themselves (RevealAsync). The status change is conditional, so a second click, or a second admin,
    // gets 409.
    public async Task<SupportResult<PasswordResetRequest>> ApproveAsync(Guid requestId, Guid adminId)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now + TemporaryPasswords.Lifetime;
        var claimed = await db.PasswordResetRequests
            .Where(r => r.Id == requestId && r.Status == PasswordResetStatuses.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, PasswordResetStatuses.Sent)
                .SetProperty(r => r.HandledById, adminId)
                .SetProperty(r => r.HandledAt, now)
                .SetProperty(r => r.TemporaryPasswordExpiresAt, expiresAt));
        if (claimed == 0)
        {
            return await db.PasswordResetRequests.AnyAsync(r => r.Id == requestId)
                ? new(StatusCodes.Status409Conflict, "This request has already been handled.")
                : new(StatusCodes.Status404NotFound, "Request not found.");
        }
        var request = await db.PasswordResetRequests.Include(r => r.User).SingleAsync(r => r.Id == requestId);
        notifications.Notify(request.UserId, "Password request approved",
            "An admin approved your password request. On the sign-in form, choose Forgot password? to get your temporary password, then set a new one.",
            NotificationTypes.Success, category: NotificationCategories.Account, senderId: adminId, relatedId: request.Id);
        await db.SaveChangesAsync();
        return new(StatusCodes.Status200OK, "Request approved. The user can now get their temporary password.", request);
    }

    // The person who asked (proving the request's secret) gets a temporary password for an approved request.
    // It is made now, stored only as a bcrypt hash and returned once; asking again makes a new one, and the
    // previous one stops working. The account's old password stops working and its sessions end.
    public async Task<SupportResult<(string Password, DateTime ExpiresAt)>> RevealAsync(Guid requestId, string secret)
    {
        var request = await FindWithSecretAsync(requestId, secret);
        if (request is null)
        {
            return new(StatusCodes.Status404NotFound, "Request not found. Please send a new request.");
        }
        if (request.Status != PasswordResetStatuses.Sent)
        {
            return request.Status == PasswordResetStatuses.Pending
                ? new(StatusCodes.Status409Conflict, "The admin has not approved this request yet.")
                : new(StatusCodes.Status409Conflict, "This request is closed. Please send a new request.");
        }
        var now = DateTime.UtcNow;
        if (request.TemporaryPasswordExpiresAt is not { } expiresAt || expiresAt <= now)
        {
            return new(StatusCodes.Status410Gone, "This approval has expired. Please send a new request.");
        }

        var password = TemporaryPasswords.Issue(request.User!, now, expiresAt);
        await db.SaveChangesAsync();
        await TemporaryPasswords.RevokeSessionsAsync(db, request.UserId);
        return new(StatusCodes.Status200OK, "Here is your temporary password.", (password, expiresAt));
    }

    public async Task<SupportResult<PasswordResetRequest>> DismissAsync(Guid requestId, Guid adminId, string? reason)
    {
        var request = await db.PasswordResetRequests.Include(r => r.User).FirstOrDefaultAsync(r => r.Id == requestId);
        if (request is null)
        {
            return new(StatusCodes.Status404NotFound, "Request not found.");
        }
        if (request.Status != PasswordResetStatuses.Pending)
        {
            return new(StatusCodes.Status409Conflict, "This request has already been handled.");
        }
        request.Status = PasswordResetStatuses.Dismissed;
        request.AdminNote = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        request.HandledById = adminId;
        request.HandledAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return new(StatusCodes.Status200OK, "Request dismissed.", request);
    }

    // After the user replaces their temporary password: close the request.
    public Task<int> CompleteAsync(Guid userId)
    {
        var now = DateTime.UtcNow;
        return db.PasswordResetRequests
            .Where(r => r.UserId == userId && r.Status == PasswordResetStatuses.Sent)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, PasswordResetStatuses.Completed)
                .SetProperty(r => r.CompletedAt, now));
    }

    private Task<List<Guid>> AdminIdsAsync() => db.Users.Where(u => u.Role == UserRoles.Admin).Select(u => u.Id).ToListAsync();
}

public record SupportThreadSummary(User User, SupportMessage LastMessage, int Unread, int EmergencyUnread);

// The conversation between each user and the admins.
public class SupportService(AppDbContext db, NotificationService notifications)
{
    public const int MaxLength = 2000;
    private const int PageSize = 200;

    // The user a conversation belongs to, for the admin view.
    public Task<User?> ThreadOwnerAsync(Guid userId) => db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

    // The latest messages of a user's conversation, oldest first.
    public async Task<List<SupportMessage>> ThreadAsync(Guid userId)
    {
        var latest = await db.SupportMessages
            .Where(m => m.UserId == userId)
            .Include(m => m.Sender)
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Take(PageSize)
            .AsNoTracking()
            .ToListAsync();
        latest.Reverse();
        return latest;
    }

    // Adds a message to the user's conversation and tells the other side. fromAdmin: an admin is writing to the
    // user; otherwise the user is writing to the admins.
    public async Task<SupportResult<SupportMessage>> SendAsync(Guid threadUserId, Guid senderId, bool fromAdmin, string? text, bool emergency)
    {
        text = text?.Trim() ?? "";
        if (text.Length == 0)
        {
            return new(StatusCodes.Status400BadRequest, "Please write a message.");
        }
        if (text.Length > MaxLength)
        {
            return new(StatusCodes.Status400BadRequest, $"Messages can be at most {MaxLength} characters.");
        }
        var owner = await db.Users.FindAsync(threadUserId);
        if (owner is null)
        {
            return new(StatusCodes.Status404NotFound, "User not found.");
        }
        if (owner.Role == UserRoles.Admin)
        {
            return new(StatusCodes.Status400BadRequest, "Admins do not have a support conversation.");
        }

        var message = new SupportMessage
        {
            Id = Guid.NewGuid(),
            UserId = owner.Id,
            SenderId = senderId,
            FromAdmin = fromAdmin,
            Text = text,
            IsEmergency = emergency,
            CreatedAt = DateTime.UtcNow,
        };
        db.SupportMessages.Add(message);

        var type = emergency ? NotificationTypes.Error : NotificationTypes.Info;
        if (fromAdmin)
        {
            notifications.Notify(owner.Id, emergency ? "Emergency message from admin" : "New message from admin", Preview(text),
                type, actionUrl: "/support", category: NotificationCategories.Account, senderId: senderId, relatedId: message.Id);
        }
        else
        {
            var admins = await db.Users.Where(u => u.Role == UserRoles.Admin).Select(u => u.Id).ToListAsync();
            foreach (var adminId in admins)
            {
                notifications.Notify(adminId, emergency ? $"Emergency message from {owner.Name}" : $"Message from {owner.Name}", Preview(text),
                    type, actionUrl: "/admin", category: NotificationCategories.Account, senderId: owner.Id, relatedId: message.Id);
            }
        }
        await db.SaveChangesAsync();
        message.Sender = await db.Users.FindAsync(senderId);
        return new(StatusCodes.Status201Created, "Message sent.", message);
    }

    // Marks the other side's messages in a conversation as read.
    public Task<int> MarkReadAsync(Guid threadUserId, bool readerIsAdmin)
    {
        var now = DateTime.UtcNow;
        return db.SupportMessages
            .Where(m => m.UserId == threadUserId && m.FromAdmin != readerIsAdmin && m.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReadAt, now));
    }

    // Admin messages the user has not read yet.
    public Task<int> UnreadForUserAsync(Guid userId) =>
        db.SupportMessages.CountAsync(m => m.UserId == userId && m.FromAdmin && m.ReadAt == null);

    // Every conversation for the admin inbox: unread emergencies first, then unread, then the most recent.
    public async Task<List<SupportThreadSummary>> ThreadsAsync()
    {
        var stats = await db.SupportMessages
            .GroupBy(m => m.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                LastAt = g.Max(m => m.CreatedAt),
                Unread = g.Count(m => !m.FromAdmin && m.ReadAt == null),
                EmergencyUnread = g.Count(m => !m.FromAdmin && m.ReadAt == null && m.IsEmergency),
            })
            .OrderByDescending(s => s.EmergencyUnread > 0).ThenByDescending(s => s.Unread > 0).ThenByDescending(s => s.LastAt)
            .Take(100)
            .ToListAsync();
        if (stats.Count == 0)
        {
            return [];
        }
        var ids = stats.Select(s => s.UserId).ToList();
        var users = await db.Users.Where(u => ids.Contains(u.Id)).AsNoTracking().ToDictionaryAsync(u => u.Id);
        var lastMessages = await db.SupportMessages
            .Where(m => ids.Contains(m.UserId))
            .GroupBy(m => m.UserId)
            .Select(g => g.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).First())
            .AsNoTracking()
            .ToDictionaryAsync(m => m.UserId);
        return stats
            .Where(s => users.ContainsKey(s.UserId) && lastMessages.ContainsKey(s.UserId))
            .Select(s => new SupportThreadSummary(users[s.UserId], lastMessages[s.UserId], s.Unread, s.EmergencyUnread))
            .ToList();
    }

    private static string Preview(string text) => text.Length <= 140 ? text : text[..137] + "…";
}
