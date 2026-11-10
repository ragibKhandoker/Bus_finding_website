using System.ComponentModel.DataAnnotations;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Server.ViewModels;

// F17: what the Lost & Found pages read and send.

public class LostFoundViewModel
{
    public string Id { get; init; } = "";
    public string ReportType { get; init; } = "";
    public string ItemName { get; init; } = "";
    public string Category { get; init; } = "";
    public string? BusId { get; init; }
    public string BusName { get; init; } = "";
    public string Route { get; init; } = "";
    public string Location { get; init; } = "";
    // "2026-11-09" and "18:30" in Dhaka time.
    public string IncidentDate { get; init; } = "";
    public string? IncidentTime { get; init; }
    public string Description { get; init; } = "";
    public string? ImageUrl { get; init; }
    public string ContactInformation { get; init; } = "";
    public string Status { get; init; } = "";
    public string? AdminNote { get; init; }
    // Only the first name of the person who made the report; never their email or account details.
    public string ReporterName { get; init; } = "";
    public bool IsMine { get; init; }
    public bool CanManage { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime? ResolvedAt { get; init; }

    public static LostFoundViewModel From(LostFound r, Guid viewerId, bool viewerIsAdmin) => new()
    {
        Id = r.Id.ToString(), ReportType = r.ReportType, ItemName = r.ItemName, Category = r.Category, BusId = r.BusId?.ToString(),
        BusName = r.BusName, Route = r.Route, Location = r.Location, IncidentDate = r.IncidentDate.ToString("yyyy-MM-dd"),
        IncidentTime = r.IncidentTime?.ToString("HH:mm"), Description = r.Description,
        ImageUrl = r.ImageKey is null ? null : LostFoundService.ImageUrl(r.ImageKey), ContactInformation = r.ContactInformation,
        Status = r.Status, AdminNote = r.AdminNote, ReporterName = RewardEngine.FirstName(r.User?.Name ?? ""), IsMine = r.UserId == viewerId,
        CanManage = r.UserId == viewerId || viewerIsAdmin, CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt, ResolvedAt = r.ResolvedAt,
    };
}

// Create or update a report. The user always comes from the sign-in token, never from this body. image is an
// optional data URL ("data:image/jpeg;base64,…"); on an update, leaving it out keeps the picture and removeImage
// takes it away.
public class LostFoundRequest
{
    [Required(ErrorMessage = "Choose Lost or Found."), StringLength(10)]
    public string ReportType { get; set; } = "";

    [Required(ErrorMessage = "Enter the item's name."), StringLength(100, MinimumLength = 2, ErrorMessage = "The item name must be 2 to 100 characters.")]
    public string ItemName { get; set; } = "";

    [Required(ErrorMessage = "Choose a category."), StringLength(30)]
    public string Category { get; set; } = "";

    [Required(ErrorMessage = "Choose the bus.")]
    public Guid? BusId { get; set; }

    [Required(ErrorMessage = "Enter where it happened."), StringLength(200, MinimumLength = 2, ErrorMessage = "The location must be 2 to 200 characters.")]
    public string Location { get; set; } = "";

    [Required(ErrorMessage = "Choose the date."), StringLength(10)]
    public string IncidentDate { get; set; } = "";

    [StringLength(5)]
    public string? IncidentTime { get; set; }

    [Required(ErrorMessage = "Describe the item."), StringLength(1000, MinimumLength = 10, ErrorMessage = "The description must be 10 to 1000 characters.")]
    public string Description { get; set; } = "";

    [Required(ErrorMessage = "Enter how people can contact you."), StringLength(150, MinimumLength = 5, ErrorMessage = "The contact information must be 5 to 150 characters.")]
    public string ContactInformation { get; set; } = "";

    // About 5 MB of picture is about 7 million base64 characters.
    [StringLength(7_200_000)]
    public string? Image { get; set; }

    public bool RemoveImage { get; set; }

    // Only on an update: the owner may mark the report resolved, closed or open again.
    [StringLength(20)]
    public string? Status { get; set; }
}

public class LostFoundStatusRequest
{
    [Required(ErrorMessage = "Choose a status."), StringLength(20)]
    public string Status { get; set; } = "";

    [StringLength(300)]
    public string? Note { get; set; }
}

// ---- Messages about a report ----

public class LostFoundReactionViewModel
{
    public string Emoji { get; init; } = "";
    public int Count { get; init; }
    public bool Mine { get; init; }

    // One entry per emoji used on the message, in the order of the emoji picker.
    public static List<LostFoundReactionViewModel> Summary(IEnumerable<LostFoundMessageReaction> reactions, Guid me) => reactions
        .GroupBy(r => r.Emoji)
        .OrderBy(g => Array.IndexOf(LostFoundMessageReaction.Allowed, g.Key))
        .Select(g => new LostFoundReactionViewModel { Emoji = g.Key, Count = g.Count(), Mine = g.Any(r => r.UserId == me) })
        .ToList();
}

public class LostFoundMessageViewModel
{
    public string Id { get; init; } = "";
    public string Text { get; init; } = "";
    public string? ImageUrl { get; init; }
    public bool Mine { get; init; }
    public DateTime CreatedAt { get; init; }
    public List<LostFoundReactionViewModel> Reactions { get; init; } = [];

    public static LostFoundMessageViewModel From(LostFoundMessage m, Guid me, IEnumerable<LostFoundMessageReaction>? reactions = null) => new()
    {
        Id = m.Id.ToString(), Text = m.Text, ImageUrl = m.ImageKey is null ? null : LostFoundService.ImageUrl(m.ImageKey), Mine = m.SenderId == me,
        CreatedAt = m.CreatedAt, Reactions = LostFoundReactionViewModel.Summary(reactions ?? [], me),
    };
}

public class LostFoundConversationViewModel
{
    public string Id { get; init; } = "";
    public string ReportId { get; init; } = "";
    public string ItemName { get; init; } = "";
    public string ReportType { get; init; } = "";
    public string ReportStatus { get; init; } = "";
    // Only first names: who I am talking to, and whether the report is mine.
    public string WithName { get; init; } = "";
    public bool IAmOwner { get; init; }
    public int Unread { get; init; }
    public LostFoundMessageViewModel? LastMessage { get; init; }
    public DateTime LastMessageAt { get; init; }

    public static LostFoundConversationViewModel From(LostFoundConversation c, Guid me, int unread = 0, LostFoundMessage? last = null) => new()
    {
        Id = c.Id.ToString(), ReportId = c.ReportId.ToString(), ItemName = c.Report?.ItemName ?? "", ReportType = c.Report?.ReportType ?? "",
        ReportStatus = c.Report?.Status ?? "", IAmOwner = c.OwnerId == me,
        WithName = RewardEngine.FirstName((c.OwnerId == me ? c.OtherUser?.Name : c.Owner?.Name) ?? ""),
        Unread = unread, LastMessage = last is null ? null : LostFoundMessageViewModel.From(last, me), LastMessageAt = c.LastMessageAt,
    };
}

// Words, a picture (data URL, at most 5 MB), or both.
public class LostFoundMessageRequest
{
    [StringLength(LostFoundMessage.MaxLength, ErrorMessage = "A message can be at most 1000 characters.")]
    public string? Text { get; set; }

    [StringLength(7_200_000)]
    public string? Image { get; set; }
}

// emoji: one of 👍 ❤️ 😂 😮 😢 🙏; empty takes the reaction away.
public class LostFoundReactionRequest
{
    [StringLength(16)]
    public string? Emoji { get; set; }
}
