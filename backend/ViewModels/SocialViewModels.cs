using System.ComponentModel.DataAnnotations;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.ViewModels;

public class NotificationViewModel : EntityViewModel
{
    public string UserId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public string Type { get; init; } = "";
    public string Category { get; init; } = "";
    public string? SenderId { get; init; }
    public string? RelatedId { get; init; }
    public bool IsRead { get; init; }
    // A page in the app ("/friends") or, for an SOS, a Google Maps link to the sender's location.
    public string? ActionUrl { get; init; }
    public DateTime CreatedAt { get; init; }

    public static NotificationViewModel From(Notification n) => new()
    {
        Id = n.Id.ToString(),
        UserId = n.UserId.ToString(),
        Title = n.Title,
        Message = n.Message,
        Type = n.Type,
        Category = n.Category,
        SenderId = n.SenderId?.ToString(),
        RelatedId = n.RelatedId?.ToString(),
        IsRead = n.IsRead,
        ActionUrl = n.ActionUrl,
        CreatedAt = n.CreatedAt,
    };
}

// The sender is the signed-in user. Latitude and longitude come from the browser and are required; serviceName
// says what kind of help is needed. Fields older clients send (userId, userName, source, serviceType) are ignored.
public class SosRequest
{
    [Required(ErrorMessage = "Your location is needed to send an SOS. Allow location access and try again.")]
    [Range(-90, 90, ErrorMessage = "That location is not valid.")]
    public double? Latitude { get; set; }

    [Required(ErrorMessage = "Your location is needed to send an SOS. Allow location access and try again.")]
    [Range(-180, 180, ErrorMessage = "That location is not valid.")]
    public double? Longitude { get; set; }

    [StringLength(50)]
    public string? ServiceName { get; set; }

    [StringLength(200, ErrorMessage = "The note can be at most 200 characters.")]
    public string? Message { get; set; }
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

// A verification request. Status is the stored value ("hold" while pending); State is "pending", "approved" or
// "rejected". ImageUrl is an API path that needs the owner's or an admin's token, never a public file link.
public class VerificationViewModel : EntityViewModel
{
    public string UserId { get; init; } = "";
    public string UserName { get; init; } = "";
    public string UserEmail { get; init; } = "";
    public string DocumentType { get; init; } = "";
    public string InstitutionName { get; init; } = "";
    public string InstitutionId { get; init; } = "";
    public string Gmail { get; init; } = "";
    public string? ImageUrl { get; init; }
    public bool HasImage { get; init; }
    public string Status { get; init; } = "";
    public string State { get; init; } = "";
    public string? RejectionReason { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ReviewedAt { get; init; }

    public static VerificationViewModel From(Verification v)
    {
        var hasImage = v.ImageId is not null || !string.IsNullOrWhiteSpace(v.ImageUrl);
        return new()
        {
            Id = v.Id.ToString(),
            UserId = v.UserId.ToString(),
            UserName = v.UserName,
            UserEmail = v.UserEmail,
            DocumentType = v.DocumentType,
            InstitutionName = v.InstitutionName,
            InstitutionId = v.InstitutionId,
            Gmail = v.Gmail,
            ImageUrl = hasImage ? $"/verify/{v.Id}/image" : null,
            HasImage = hasImage,
            Status = v.Status,
            State = VerificationStatus.Label(v.Status),
            RejectionReason = v.RejectionReason,
            CreatedAt = v.CreatedAt,
            ReviewedAt = v.ReviewedAt,
        };
    }
}

public class UploadImageRequest
{
    [Required(ErrorMessage = "Choose a JPEG, PNG or WebP image.")]
    public string Image { get; set; } = "";
}

// documentType is "student_pass" (the default, needs institution name and ID) or "nid". imageId comes from
// upload-image and must be the caller's own upload.
public class SubmitVerificationRequest
{
    [StringLength(20)]
    public string? DocumentType { get; set; }

    [Required(ErrorMessage = "Upload a photo of your document first.")]
    public Guid? ImageId { get; set; }

    [StringLength(200)]
    public string? InstitutionName { get; set; }

    [StringLength(100)]
    public string? InstitutionId { get; set; }

    [EmailAddress(ErrorMessage = "Please enter a valid email address."), StringLength(255)]
    public string? Gmail { get; set; }
}

public class VerificationIdRequest
{
    [Required]
    public Guid? VerificationId { get; set; }
}

public class RejectVerificationRequest : VerificationIdRequest
{
    [StringLength(500, ErrorMessage = "The reason can be at most 500 characters.")]
    public string? Reason { get; set; }
}
