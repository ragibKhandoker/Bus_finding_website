using System.ComponentModel.DataAnnotations;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.ViewModels;

// F18: what the complaint pages read and send.

public class ComplaintEvidenceViewModel
{
    public string Id { get; init; } = "";
    // Needs the sign-in token: the page fetches it and shows it as a blob.
    public string Url { get; init; } = "";
}

public class ComplaintHistoryViewModel
{
    public string? FromStatus { get; init; }
    public string ToStatus { get; init; } = "";
    public string? Note { get; init; }
    public string ActorRole { get; init; } = "";
    public string ActorName { get; init; } = "";
    public DateTime CreatedAt { get; init; }

    // Users see "SmartCommute admin"; admins see which admin it was.
    public static ComplaintHistoryViewModel From(ComplaintHistory h, bool forAdmin) => new()
    {
        FromStatus = h.FromStatus, ToStatus = h.ToStatus, Note = h.Note, ActorRole = h.ActorRole, CreatedAt = h.CreatedAt,
        ActorName = h.ActorRole == "admin" ? (forAdmin ? h.Actor?.Name ?? "Admin" : "SmartCommute admin") : (h.Actor?.Name ?? "You"),
    };
}

public class ComplaintUserViewModel
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
    public string? Phone { get; init; }
}

public class ComplaintViewModel
{
    public string Id { get; init; } = "";
    // A short reference for people to quote, e.g. "CMP-3F2A9C1B".
    public string Code { get; init; } = "";
    public string? BusId { get; init; }
    public string BusName { get; init; } = "";
    public string? RouteNumber { get; init; }
    public string RouteName { get; init; } = "";
    public string? StopName { get; init; }
    public string Category { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Description { get; init; } = "";
    public string IncidentDate { get; init; } = "";
    public string IncidentLocation { get; init; } = "";
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public string Status { get; init; } = "";
    public string? AdminNotes { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime? ResolvedAt { get; init; }
    public bool CanDelete { get; init; }
    public int EvidenceCount { get; init; }
    public List<ComplaintEvidenceViewModel> Evidence { get; init; } = [];
    public List<ComplaintHistoryViewModel> History { get; init; } = [];
    // Only for admins.
    public ComplaintUserViewModel? User { get; init; }
    public List<string> NextStatuses { get; init; } = [];

    public static string CodeOf(Guid id) => "CMP-" + id.ToString("N")[..8].ToUpperInvariant();

    public static ComplaintViewModel From(Complaint c, Guid viewerId, bool forAdmin, bool withDetails = false) => new()
    {
        Id = c.Id.ToString(), Code = CodeOf(c.Id), BusId = c.BusId?.ToString(), BusName = c.BusName, RouteNumber = c.RouteNumber, RouteName = c.RouteName,
        StopName = c.StopName, Category = c.Category, Subject = c.Subject, Description = c.Description, IncidentDate = c.IncidentDate.ToString("yyyy-MM-dd"),
        IncidentLocation = c.IncidentLocation, Latitude = c.Latitude, Longitude = c.Longitude, Status = c.Status, AdminNotes = c.AdminNotes,
        CreatedAt = c.CreatedAt, UpdatedAt = c.UpdatedAt, ResolvedAt = c.ResolvedAt,
        CanDelete = c.UserId == viewerId && c.Status == ComplaintStatus.Pending,
        EvidenceCount = c.Evidence.Count,
        Evidence = c.Evidence.OrderBy(e => e.CreatedAt).Select(e => new ComplaintEvidenceViewModel { Id = e.Id.ToString(), Url = $"/api/complaints/{c.Id}/evidence/{e.Id}" }).ToList(),
        History = withDetails ? c.History.OrderBy(h => h.CreatedAt).Select(h => ComplaintHistoryViewModel.From(h, forAdmin)).ToList() : [],
        User = forAdmin && c.User is { } u ? new ComplaintUserViewModel { Id = u.Id.ToString(), Name = u.Name, Email = u.Email, Phone = u.Phone } : null,
        NextStatuses = forAdmin && ComplaintStatus.Next.TryGetValue(c.Status, out var next) ? [.. next] : [],
    };
}

// The user always comes from the sign-in token, never from this body. evidence: up to 3 pictures as data URLs.
public class CreateComplaintRequest
{
    [Required(ErrorMessage = "Choose the bus and its route.")]
    public Guid? BusId { get; set; }

    [Required(ErrorMessage = "Choose a category."), StringLength(60)]
    public string Category { get; set; } = "";

    [Required(ErrorMessage = "Enter a subject."), StringLength(150, MinimumLength = 5, ErrorMessage = "The subject must be 5 to 150 characters.")]
    public string Subject { get; set; } = "";

    [Required(ErrorMessage = "Describe what happened."), StringLength(2000, MinimumLength = 20, ErrorMessage = "The description must be 20 to 2000 characters.")]
    public string Description { get; set; } = "";

    [Required(ErrorMessage = "Choose the date it happened."), StringLength(10)]
    public string IncidentDate { get; set; } = "";

    [Required(ErrorMessage = "Enter where it happened."), StringLength(200, MinimumLength = 2, ErrorMessage = "The location must be 2 to 200 characters.")]
    public string IncidentLocation { get; set; } = "";

    [StringLength(150)]
    public string? StopName { get; set; }

    [Range(-90, 90)] public double? Latitude { get; set; }
    [Range(-180, 180)] public double? Longitude { get; set; }

    [MaxLength(3, ErrorMessage = "Add at most 3 pictures.")]
    public List<string>? Evidence { get; set; }
}

public class ComplaintStatusRequest
{
    [Required(ErrorMessage = "Choose the new status."), StringLength(20)]
    public string Status { get; set; } = "";

    [Required(ErrorMessage = "Write a note for the user."), StringLength(1000, MinimumLength = 5, ErrorMessage = "The note must be 5 to 1000 characters.")]
    public string AdminNotes { get; set; } = "";
}
