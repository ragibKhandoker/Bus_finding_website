using System.ComponentModel.DataAnnotations;

namespace SmartCommute.Server.Models;

public static class PasswordResetStatuses
{
    // Waiting for an admin.
    public const string Pending = "pending";
    // Approved by an admin; the user can collect a temporary password and has not set a new password yet.
    public const string Sent = "sent";
    // The user signed in with the temporary password and chose a new one.
    public const string Completed = "completed";
    // An admin turned the request down, or a newer request replaced it.
    public const string Dismissed = "dismissed";

    public static readonly string[] All = [Pending, Sent, Completed, Dismissed];
}

// "I forgot my password" sent to the admins from the sign-in form. The person asking is not signed in, so the
// request is tied to a random secret their browser keeps; only a hash of it is stored, and only someone holding
// the secret can see the temporary password an admin issues.
public class PasswordResetRequest
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    // Optional note from the user to the admin.
    [MaxLength(500)]
    public string? Note { get; set; }

    // SHA-256 (hex) of the secret given to the requester's browser.
    [MaxLength(64)]
    public string SecretHash { get; set; } = "";

    [MaxLength(20)]
    public string Status { get; set; } = PasswordResetStatuses.Pending;

    // When an approved request stops being usable. The temporary password itself is never stored: the account
    // keeps only its bcrypt hash (User.PasswordHash).
    public DateTime? TemporaryPasswordExpiresAt { get; set; }

    // Why an admin dismissed the request, shown to the requester.
    [MaxLength(300)]
    public string? AdminNote { get; set; }

    public Guid? HandledById { get; set; }
    public User? HandledBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? HandledAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

// One message in the conversation between a user and the admins. Each non-admin user has one conversation,
// identified by UserId; any admin can read and answer it.
public class SupportMessage
{
    public Guid Id { get; set; }

    // The user whose conversation this is (never an admin).
    public Guid UserId { get; set; }
    public User? User { get; set; }

    // Who wrote it: the user, or the admin who answered. Null if that admin's account was deleted.
    public Guid? SenderId { get; set; }
    public User? Sender { get; set; }

    public bool FromAdmin { get; set; }

    [MaxLength(2000)]
    public string Text { get; set; } = "";

    // Marked urgent by the sender: shown first and highlighted, and the notification is a warning.
    public bool IsEmergency { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // When the other side read it.
    public DateTime? ReadAt { get; set; }
}
