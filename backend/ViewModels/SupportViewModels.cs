using System.ComponentModel.DataAnnotations;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Server.ViewModels;

// ---- Forgot password ----

public class ForgotPasswordRequest
{
    [Required(ErrorMessage = "Please enter your email address.")]
    [RegularExpression(@"^\s*[^@\s]+@[^@\s]+\.[^@\s]+\s*$", ErrorMessage = "Please enter a valid email address.")]
    [StringLength(255)]
    public string Email { get; set; } = "";

    [StringLength(500, ErrorMessage = "The note can be at most 500 characters.")]
    public string? Message { get; set; }
}

public class ResetStatusRequest
{
    [Required]
    public Guid RequestId { get; set; }

    [Required]
    [StringLength(128)]
    public string Secret { get; set; } = "";
}

public class DismissResetRequest
{
    [StringLength(300, ErrorMessage = "The reason can be at most 300 characters.")]
    public string? Reason { get; set; }
}

// The status shown for a request: an approved request is "approved", or "expired" once its time is up.
public static class ResetStatusNames
{
    public const string Approved = "approved";
    public const string Expired = "expired";

    public static string Of(PasswordResetRequest r, DateTime now) => r.Status != PasswordResetStatuses.Sent
        ? r.Status
        : r.TemporaryPasswordExpiresAt > now ? Approved : Expired;
}

// What the person who asked sees. It never contains a password: an approved request's temporary password is
// collected separately (POST /auth/forgot-password/reveal) and shown once.
public class ResetStatusViewModel : EntityViewModel
{
    public string Status { get; init; } = "";
    public string Email { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime? HandledAt { get; init; }
    public string? AdminNote { get; init; }
    public DateTime? ExpiresAt { get; init; }

    public static ResetStatusViewModel From(PasswordResetRequest r, DateTime now)
    {
        var status = ResetStatusNames.Of(r, now);
        return new()
        {
            Id = r.Id.ToString(),
            Status = status,
            Email = r.User?.Email ?? "",
            CreatedAt = r.CreatedAt,
            HandledAt = r.HandledAt,
            AdminNote = r.AdminNote,
            ExpiresAt = status == ResetStatusNames.Approved ? r.TemporaryPasswordExpiresAt : null,
        };
    }
}

// What the admin list shows. Never a password: admins approve requests but never see temporary passwords.
public class AdminResetRequestViewModel : EntityViewModel
{
    public string Status { get; init; } = "";
    public string? Note { get; init; }
    public string? AdminNote { get; init; }
    public AdminUserViewModel? User { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? HandledAt { get; init; }
    public DateTime? ExpiresAt { get; init; }

    public static AdminResetRequestViewModel From(PasswordResetRequest r) => new()
    {
        Id = r.Id.ToString(),
        Status = ResetStatusNames.Of(r, DateTime.UtcNow),
        Note = r.Note,
        AdminNote = r.AdminNote,
        User = r.User is null ? null : AdminUserViewModel.From(r.User),
        CreatedAt = r.CreatedAt,
        HandledAt = r.HandledAt,
        ExpiresAt = r.TemporaryPasswordExpiresAt,
    };
}

// ---- Admin and user messages ----

public class SupportMessageRequest
{
    [Required(ErrorMessage = "Please write a message.")]
    [StringLength(SupportService.MaxLength, ErrorMessage = "Messages can be at most 2000 characters.")]
    public string Text { get; set; } = "";

    public bool Emergency { get; set; }
}

public class SupportMessageViewModel : EntityViewModel
{
    public string UserId { get; init; } = "";
    public string? SenderId { get; init; }
    public string SenderName { get; init; } = "";
    public bool FromAdmin { get; init; }
    public string Text { get; init; } = "";
    public bool Emergency { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ReadAt { get; init; }

    public static SupportMessageViewModel From(SupportMessage m) => new()
    {
        Id = m.Id.ToString(),
        UserId = m.UserId.ToString(),
        SenderId = m.SenderId?.ToString(),
        // Users see "Admin" rather than which admin answered.
        SenderName = m.FromAdmin ? "Admin" : m.Sender?.Name ?? "",
        FromAdmin = m.FromAdmin,
        Text = m.Text,
        Emergency = m.IsEmergency,
        CreatedAt = m.CreatedAt,
        ReadAt = m.ReadAt,
    };
}

public class SupportThreadViewModel
{
    public AdminUserViewModel User { get; init; } = new();
    public SupportMessageViewModel LastMessage { get; init; } = new();
    public int Unread { get; init; }
    public int EmergencyUnread { get; init; }

    public static SupportThreadViewModel From(SupportThreadSummary s) => new()
    {
        User = AdminUserViewModel.From(s.User),
        LastMessage = SupportMessageViewModel.From(s.LastMessage),
        Unread = s.Unread,
        EmergencyUnread = s.EmergencyUnread,
    };
}
