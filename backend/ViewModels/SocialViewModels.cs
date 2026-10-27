using System.ComponentModel.DataAnnotations;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.ViewModels;

public class NotificationViewModel : EntityViewModel
{
    public string UserId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public string Type { get; init; } = "";
    public bool IsRead { get; init; }
    public string? ActionUrl { get; init; }
    public DateTime CreatedAt { get; init; }

    public static NotificationViewModel From(Notification n) => new()
    {
        Id = n.Id.ToString(),
        UserId = n.UserId.ToString(),
        Title = n.Title,
        Message = n.Message,
        Type = n.Type,
        IsRead = n.IsRead,
        ActionUrl = n.ActionUrl,
        CreatedAt = n.CreatedAt,
    };
}

public class SosRequest
{
    [Required]
    public Guid? UserId { get; set; }

    [StringLength(100)]
    public string? UserName { get; set; }

    [StringLength(200)]
    public string? Source { get; set; }

    [Range(-90, 90)]
    public double? Latitude { get; set; }

    [Range(-180, 180)]
    public double? Longitude { get; set; }

    [StringLength(30)]
    public string? ServiceType { get; set; }

    [StringLength(50)]
    public string? ServiceName { get; set; }
}

public class ChatMessageViewModel : EntityViewModel
{
    public string ChatId { get; init; } = "";
    public string SenderId { get; init; } = "";
    // Empty when the sender was not loaded with the message.
    public string SenderName { get; init; } = "";
    public string Text { get; init; } = "";
    // The same as Text; F10 clients read "content".
    public string Content => Text;
    public bool Read { get; init; }
    public DateTime? ReadAt { get; init; }
    public DateTime CreatedAt { get; init; }

    public static ChatMessageViewModel From(ChatMessage m) => new()
    {
        Id = m.Id.ToString(),
        ChatId = m.ChatId.ToString(),
        SenderId = m.SenderId.ToString(),
        SenderName = m.Sender?.Name ?? "",
        Text = m.Text,
        Read = m.Read,
        ReadAt = m.ReadAt,
        CreatedAt = m.CreatedAt,
    };
}

// A conversation as GET /api/chat/{otherUserId} returns it. Messages holds the latest page, oldest first.
public class ChatViewModel : EntityViewModel
{
    public List<FriendProfileViewModel> Participants { get; init; } = [];
    public FriendProfileViewModel OtherParticipant { get; init; } = new();
    public List<ChatMessageViewModel> Messages { get; init; } = [];
    public bool HasMoreMessages { get; init; }
    public int UnreadCount { get; init; }
    public DateTime? LastMessageAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

public class ChatSummaryViewModel : EntityViewModel
{
    public FriendProfileViewModel OtherParticipant { get; init; } = new();
    public ChatMessageViewModel? LastMessage { get; init; }
    public int UnreadCount { get; init; }
    public DateTime? LastMessageAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

// "content" is the F10 field name; "text" is still accepted from older clients. The controller trims the
// message and refuses it when nothing is left.
public class SendMessageRequest
{
    [Required(ErrorMessage = "Choose a conversation first.")]
    public Guid? ChatId { get; set; }

    [StringLength(ChatMessage.MaxLength, ErrorMessage = "A message can be at most 2000 characters.")]
    public string? Content { get; set; }

    [StringLength(ChatMessage.MaxLength, ErrorMessage = "A message can be at most 2000 characters.")]
    public string? Text { get; set; }
}

public class VerificationViewModel : EntityViewModel
{
    public string UserId { get; init; } = "";
    public string UserName { get; init; } = "";
    public string UserEmail { get; init; } = "";
    public string InstitutionName { get; init; } = "";
    public string InstitutionId { get; init; } = "";
    public string Gmail { get; init; } = "";
    public string? ImageUrl { get; init; }
    public string Status { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime? ReviewedAt { get; init; }

    public static VerificationViewModel From(Verification v) => new()
    {
        Id = v.Id.ToString(),
        UserId = v.UserId.ToString(),
        UserName = v.UserName,
        UserEmail = v.UserEmail,
        InstitutionName = v.InstitutionName,
        InstitutionId = v.InstitutionId,
        Gmail = v.Gmail,
        ImageUrl = v.ImageUrl,
        Status = v.Status,
        CreatedAt = v.CreatedAt,
        ReviewedAt = v.ReviewedAt,
    };
}

public class UploadImageRequest
{
    [Required]
    public string Image { get; set; } = "";
}

public class SubmitVerificationRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public string InstitutionName { get; set; } = "";

    [Required, StringLength(100, MinimumLength = 1)]
    public string InstitutionId { get; set; } = "";

    [Required, EmailAddress, StringLength(255)]
    public string Gmail { get; set; } = "";

    [StringLength(500)]
    public string? ImageUrl { get; set; }
}

public class VerificationIdRequest
{
    [Required]
    public Guid? VerificationId { get; set; }
}
