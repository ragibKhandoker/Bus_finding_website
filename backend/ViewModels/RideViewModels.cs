using System.ComponentModel.DataAnnotations;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Server.ViewModels;

public class ParticipantViewModel
{
    public string UserId { get; init; } = "";
    public string UserName { get; init; } = "";
    public string Gender { get; init; } = "";
    public bool Verified { get; init; }
    public DateTime? JoinedAt { get; init; }
    // Only for the host: the rider's WhatsApp number (null when they have none). ContactVisible says the host may
    // see it, so the page can tell "no WhatsApp" apart from "not shown to you".
    public string? WhatsApp { get; init; }
    public bool ContactVisible { get; init; }
}

// A person's ride share record: their average rating from ride reviews and how many rides they completed.
public record RideUserStats(double? Rating, int ReviewCount, int CompletedRides)
{
    public static readonly RideUserStats None = new(null, 0, 0);
}

// What anyone may see about a ride's host: no email, phone or other private details.
public class RideHostViewModel
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Gender { get; init; } = "";
    public bool Verified { get; init; }
    // Average of the ride reviews this person received (null until they have one), and how many there are.
    public double? Rating { get; init; }
    public int ReviewCount { get; init; }
    public int CompletedRides { get; init; }
}

// Who is looking at a ride, decided on the server: the host, an approved rider, someone with a pending or past
// request, or nobody in particular. Private details (pickup, the host's phone) and the chat follow from it.
public record RideViewer(Guid? UserId, RideRequest? MyRequest, RideUserStats HostStats, string? HostPhone, bool Blocked, DateTime NowUtc,
    string? HostWhatsApp = null)
{
    public static RideViewer Anonymous(DateTime nowUtc) => new(null, null, RideUserStats.None, null, false, nowUtc);
}

public class RideViewerViewModel
{
    // "host", "approved", "pending", "rejected", "cancelled", "removed" or "none".
    public string Role { get; init; } = "none";
    public string? RequestId { get; init; }
    public bool CanRequest { get; init; }
    // Why Request to Join is not offered ("own", "full", "closed", "departed", "requested", "blocked", "gender", "signin").
    public string? CannotRequestReason { get; init; }
    public bool CanChat { get; init; }
    public bool CanManage { get; init; }
    public bool CanReview { get; init; }
}

// How an approved rider can reach the host outside the app. Phone follows the host's contact preference; WhatsApp is
// the host's WhatsApp number unless they chose in-app chat only (null when they have no WhatsApp number).
public class RideContactViewModel
{
    public string? Phone { get; init; }
    public string? WhatsApp { get; init; }
    public bool ChatOnly { get; init; }
}

public class RidePostViewModel : EntityViewModel
{
    // The host. UserId, UserName and Gender are kept for older clients; Host is the same person.
    public string UserId { get; init; } = "";
    public string UserName { get; init; } = "";
    public string Gender { get; init; } = "";
    public RideHostViewModel Host { get; init; } = new();
    public string Source { get; init; } = "";
    public string Destination { get; init; } = "";
    public double? SourceLat { get; init; }
    public double? SourceLng { get; init; }
    public double? DestinationLat { get; init; }
    public double? DestinationLng { get; init; }
    // UTC, and the same moment in Dhaka time ("2026-11-10T08:30+06:00"). Null for rides posted before it was asked.
    public DateTime? DepartureAt { get; init; }
    public string? DepartureLocal { get; init; }
    // Counts the host.
    public int MaxParticipants { get; init; }
    public string GenderPreference { get; init; } = "";
    public string VehicleType { get; init; } = "";
    public decimal? CostPerPerson { get; init; }
    public string? Instructions { get; init; }
    public string ContactPreference { get; init; } = "";
    // Only for the host and approved riders.
    public string? PickupDetails { get; init; }
    public RideContactViewModel? Contact { get; init; }
    public double? Distance { get; init; }
    public decimal? Fare { get; init; }
    // Stored status ("active", "full", "completed", "cancelled", "expired") and its product name (OPEN, FULL, …).
    public string Status { get; init; } = "";
    public string State { get; init; } = "";
    // Open and still taking requests: status OPEN and not yet departed.
    public bool IsOpen { get; init; }
    // Approved riders, not including the host.
    public List<ParticipantViewModel> Participants { get; init; } = [];
    // People on the ride so far, counting the host, and the places still free for riders.
    public int ParticipantCount { get; init; }
    public int SeatsLeft { get; init; }
    // Only for the host.
    public int? PendingCount { get; init; }
    public RideViewerViewModel Viewer { get; init; } = new();
    public string? CancelReason { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public DateTime? CancelledAt { get; init; }

    public static RidePostViewModel From(RidePost p) => From(p, RideViewer.Anonymous(DateTime.UtcNow));

    public static RidePostViewModel From(RidePost p, RideViewer viewer)
    {
        var isHost = viewer.UserId == p.UserId;
        var mine = isHost ? null : viewer.MyRequest;
        var approved = mine?.Status == RideRequestStatus.Accepted;
        var member = isHost || approved;
        var stats = viewer.HostStats;
        var seatsLeft = RideRequestService.SeatsLeft(p);
        var departed = p.DepartureAt is { } at && at <= viewer.NowUtc;
        var open = p.Status == RideStatus.Active && !departed;
        string? cannot = null;
        if (viewer.UserId is null) cannot = "signin";
        else if (isHost) cannot = "own";
        else if (mine is { Status: RideRequestStatus.Pending or RideRequestStatus.Accepted }) cannot = "requested";
        else if (viewer.Blocked) cannot = "blocked";
        else if (p.Status == RideStatus.Full || (p.Status == RideStatus.Active && seatsLeft == 0)) cannot = "full";
        else if (!RideStatus.IsLive(p.Status)) cannot = "closed";
        else if (departed) cannot = "departed";

        return new()
        {
            Id = p.Id.ToString(),
            UserId = p.UserId.ToString(),
            UserName = p.User?.Name ?? p.UserName,
            Gender = p.Gender,
            Host = new RideHostViewModel
            {
                Id = p.UserId.ToString(),
                Name = p.User?.Name ?? p.UserName,
                Gender = p.User?.Gender ?? p.Gender,
                Verified = p.User?.Pass ?? false,
                Rating = stats.Rating,
                ReviewCount = stats.ReviewCount,
                CompletedRides = stats.CompletedRides,
            },
            Source = p.Source,
            Destination = p.Destination,
            SourceLat = p.SourceLat,
            SourceLng = p.SourceLng,
            DestinationLat = p.DestinationLat,
            DestinationLng = p.DestinationLng,
            DepartureAt = p.DepartureAt,
            DepartureLocal = p.DepartureAt is { } d ? DhakaTime.Format(d) : null,
            MaxParticipants = p.MaxParticipants,
            GenderPreference = p.GenderPreference,
            VehicleType = p.VehicleType,
            CostPerPerson = p.CostPerPerson,
            Instructions = p.Instructions,
            ContactPreference = p.ContactPreference,
            PickupDetails = member ? p.PickupDetails : null,
            Contact = approved ? new RideContactViewModel
            {
                Phone = RideContactPreferences.SharesPhone(p.ContactPreference) ? viewer.HostPhone : null,
                WhatsApp = p.ContactPreference == RideContactPreferences.Chat ? null : viewer.HostWhatsApp,
                ChatOnly = p.ContactPreference == RideContactPreferences.Chat,
            } : null,
            Distance = p.Distance,
            Fare = p.Fare,
            Status = p.Status,
            State = RideStatus.Label(p.Status),
            IsOpen = open,
            Participants = p.Requests
                .Where(r => r.Status == RideRequestStatus.Accepted)
                .OrderBy(r => r.UpdatedAt)
                .Select(r => new ParticipantViewModel
                {
                    UserId = r.RequesterId.ToString(),
                    UserName = r.Requester?.Name ?? r.RequesterName,
                    Gender = r.Requester?.Gender ?? r.RequesterGender,
                    Verified = r.Requester?.Pass ?? false,
                    JoinedAt = r.UpdatedAt,
                    WhatsApp = isHost ? r.Requester?.WhatsApp : null,
                    ContactVisible = isHost,
                }).ToList(),
            ParticipantCount = RideRequestService.AcceptedCount(p) + 1,
            SeatsLeft = seatsLeft,
            PendingCount = isHost ? p.Requests.Count(r => r.Status == RideRequestStatus.Pending) : null,
            Viewer = new RideViewerViewModel
            {
                Role = isHost ? "host" : mine is null ? "none" : mine.RemovedByHost ? "removed" : mine.Status == RideRequestStatus.Accepted ? "approved" : mine.Status,
                RequestId = mine?.Id.ToString(),
                CanRequest = cannot is null,
                CannotRequestReason = cannot,
                CanChat = member && p.Status != RideStatus.Cancelled,
                CanManage = isHost && RideStatus.IsLive(p.Status),
                CanReview = member && p.Status == RideStatus.Completed,
            },
            CancelReason = p.CancelReason,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt,
            CompletedAt = p.CompletedAt,
            CancelledAt = p.CancelledAt,
        };
    }
}

// The fields of a new or edited ride. The host is the signed-in user; any userId, userName or gender a client still
// sends is ignored. RideRules checks every field and explains what is wrong.
public class CreateRidePostRequest
{
    public string? Source { get; set; }
    public double? SourceLat { get; set; }
    public double? SourceLng { get; set; }
    public string? Destination { get; set; }
    public double? DestinationLat { get; set; }
    public double? DestinationLng { get; set; }
    // Dhaka date and time ("2026-11-10", "08:30"), or one DepartureAt.
    public string? DepartureDate { get; set; }
    public string? DepartureTime { get; set; }
    public string? DepartureAt { get; set; }
    public int MaxParticipants { get; set; }
    public string? GenderPreference { get; set; }
    public string? VehicleType { get; set; }
    public decimal? CostPerPerson { get; set; }
    public string? Instructions { get; set; }
    public string? ContactPreference { get; set; }
    public string? PickupDetails { get; set; }

    public RideInput ToInput() => new(Source, SourceLat, SourceLng, Destination, DestinationLat, DestinationLng,
        DepartureDate, DepartureTime, DepartureAt, MaxParticipants, GenderPreference, VehicleType, CostPerPerson,
        Instructions, ContactPreference, PickupDetails);
}

public class CompleteRideRequest
{
    [Required(ErrorMessage = "Choose a ride post.")]
    public Guid? PostId { get; set; }
}

// The requester is the signed-in user; any requesterId a client still sends is ignored.
public class SendRideRequestRequest
{
    [Required(ErrorMessage = "Choose a ride post.")]
    public Guid? RidePostId { get; set; }
}

public class CancelRideRequest
{
    [MaxLength(300, ErrorMessage = "The reason can be at most 300 characters.")]
    public string? Reason { get; set; }
}

public class JoinRideRequest
{
    [MaxLength(200, ErrorMessage = "The note can be at most 200 characters.")]
    public string? Note { get; set; }
}

public class RideMessageRequest
{
    [Required(ErrorMessage = "Write a message.")]
    [MaxLength(RideMessage.MaxLength, ErrorMessage = "A message can be at most 1000 characters.")]
    public string Text { get; set; } = "";
}

public class RideReviewRequest
{
    [Required(ErrorMessage = "Choose who you are reviewing.")]
    public Guid? ReviewedUserId { get; set; }

    [Range(1, 5, ErrorMessage = "Give 1 to 5 stars.")]
    public int Rating { get; set; }

    [MaxLength(RideReview.MaxCommentLength, ErrorMessage = "A review can be at most 1000 characters.")]
    public string? Comment { get; set; }
}

public class RideReportRequest
{
    [Required(ErrorMessage = "Choose a reason.")]
    public string Reason { get; set; } = "";

    [MaxLength(1000, ErrorMessage = "The description can be at most 1000 characters.")]
    public string? Description { get; set; }
}

public class ResolveRideReportRequest
{
    [Required(ErrorMessage = "Choose reviewed or dismissed.")]
    public string Status { get; set; } = "";

    [MaxLength(500, ErrorMessage = "The note can be at most 500 characters.")]
    public string? Note { get; set; }

    // Also cancel the reported ride (only for a report about a live ride).
    public bool CancelRide { get; set; }
}

// A join request. Requester is the rider's public profile; RidePost is a summary of the ride it is for.
public class RideRequestViewModel : EntityViewModel
{
    public string RidePostId { get; init; } = "";
    public string RequesterId { get; init; } = "";
    public string RequesterName { get; init; } = "";
    public string RequesterGender { get; init; } = "";
    public RideHostViewModel Requester { get; init; } = new();
    // "pending", "accepted" (APPROVED), "rejected" or "cancelled"; State is the product name.
    public string Status { get; init; } = "";
    public string State { get; init; } = "";
    public bool RemovedByHost { get; init; }
    public string? Note { get; init; }
    // Only on the host's lists, and only for approved riders: their WhatsApp number (null when they have none).
    public string? WhatsApp { get; init; }
    public bool ContactVisible { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime? RespondedAt { get; init; }
    public RidePostSummary? RidePost { get; init; }

    public static RideRequestViewModel From(RideRequest r) => From(r, RideUserStats.None);

    public static RideRequestViewModel From(RideRequest r, RideUserStats requesterStats, bool forHost = false) => new()
    {
        WhatsApp = forHost && r.Status == RideRequestStatus.Accepted ? r.Requester?.WhatsApp : null,
        ContactVisible = forHost && r.Status == RideRequestStatus.Accepted,
        Id = r.Id.ToString(),
        RidePostId = r.RidePostId.ToString(),
        RequesterId = r.RequesterId.ToString(),
        RequesterName = r.Requester?.Name ?? r.RequesterName,
        RequesterGender = r.Requester?.Gender ?? r.RequesterGender,
        Requester = new RideHostViewModel
        {
            Id = r.RequesterId.ToString(),
            Name = r.Requester?.Name ?? r.RequesterName,
            Gender = r.Requester?.Gender ?? r.RequesterGender,
            Verified = r.Requester?.Pass ?? false,
            Rating = requesterStats.Rating,
            ReviewCount = requesterStats.ReviewCount,
            CompletedRides = requesterStats.CompletedRides,
        },
        Status = r.Status,
        State = r.RemovedByHost ? "REMOVED" : RideRequestStatus.Label(r.Status),
        RemovedByHost = r.RemovedByHost,
        Note = r.Note,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
        RespondedAt = r.Status == RideRequestStatus.Pending ? null : r.UpdatedAt,
        RidePost = r.RidePost is null ? null : RidePostSummary.From(r.RidePost),
    };
}

public class RidePostSummary : EntityViewModel
{
    public string Source { get; init; } = "";
    public string Destination { get; init; } = "";
    public string UserName { get; init; } = "";
    public string HostId { get; init; } = "";
    public string Status { get; init; } = "";
    public string State { get; init; } = "";
    public int MaxParticipants { get; init; }
    public int ParticipantCount { get; init; }
    public int SeatsLeft { get; init; }
    public DateTime? DepartureAt { get; init; }
    public string? DepartureLocal { get; init; }
    public string VehicleType { get; init; } = "";
    public decimal? CostPerPerson { get; init; }
    public DateTime CreatedAt { get; init; }

    public static RidePostSummary From(RidePost p) => new()
    {
        Id = p.Id.ToString(),
        Source = p.Source,
        Destination = p.Destination,
        UserName = p.User?.Name ?? p.UserName,
        HostId = p.UserId.ToString(),
        Status = p.Status,
        State = RideStatus.Label(p.Status),
        MaxParticipants = p.MaxParticipants,
        ParticipantCount = RideRequestService.AcceptedCount(p) + 1,
        SeatsLeft = RideRequestService.SeatsLeft(p),
        DepartureAt = p.DepartureAt,
        DepartureLocal = p.DepartureAt is { } d ? DhakaTime.Format(d) : null,
        VehicleType = p.VehicleType,
        CostPerPerson = p.CostPerPerson,
        CreatedAt = p.CreatedAt,
    };
}

public class RideMessageViewModel : EntityViewModel
{
    public string RidePostId { get; init; } = "";
    public string SenderId { get; init; } = "";
    public string SenderName { get; init; } = "";
    public bool FromHost { get; init; }
    public string Text { get; init; } = "";
    public DateTime CreatedAt { get; init; }

    public static RideMessageViewModel From(RideMessage m, Guid hostId) => new()
    {
        Id = m.Id.ToString(),
        RidePostId = m.RidePostId.ToString(),
        SenderId = m.SenderId.ToString(),
        SenderName = m.Sender?.Name ?? "",
        FromHost = m.SenderId == hostId,
        Text = m.Text,
        CreatedAt = m.CreatedAt,
    };
}

public class RideReviewViewModel : EntityViewModel
{
    public string RidePostId { get; init; } = "";
    public string ReviewerId { get; init; } = "";
    public string ReviewerName { get; init; } = "";
    public string ReviewedUserId { get; init; } = "";
    public string ReviewedUserName { get; init; } = "";
    public int Rating { get; init; }
    public string Comment { get; init; } = "";
    public string? RideSource { get; init; }
    public string? RideDestination { get; init; }
    public DateTime CreatedAt { get; init; }

    public static RideReviewViewModel From(RideReview r) => new()
    {
        Id = r.Id.ToString(),
        RidePostId = r.RidePostId.ToString(),
        ReviewerId = r.ReviewerId.ToString(),
        ReviewerName = r.Reviewer?.Name ?? "",
        ReviewedUserId = r.ReviewedUserId.ToString(),
        ReviewedUserName = r.ReviewedUser?.Name ?? "",
        Rating = r.Rating,
        Comment = r.Comment,
        RideSource = r.RidePost?.Source,
        RideDestination = r.RidePost?.Destination,
        CreatedAt = r.CreatedAt,
    };
}

// A person's public ride share profile: never their email or phone.
public class RideProfileViewModel
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Gender { get; init; } = "";
    public bool Verified { get; init; }
    public DateTime MemberSince { get; init; }
    public double? Rating { get; init; }
    public int ReviewCount { get; init; }
    public int CompletedRides { get; init; }
    public int HostedRides { get; init; }
    public bool BlockedByMe { get; init; }
    // What the person wrote about themselves, and whether an admin checked their NID.
    public string? Bio { get; init; }
    public bool IdentityVerified { get; init; }
    public bool IsMe { get; init; }
    public List<RideReviewViewModel> Reviews { get; init; } = [];
}

public class RideReportViewModel : EntityViewModel
{
    public string? RidePostId { get; init; }
    public string? RideSummary { get; init; }
    public string ReporterId { get; init; } = "";
    public string ReporterName { get; init; } = "";
    public string? ReportedUserId { get; init; }
    public string? ReportedUserName { get; init; }
    public string Reason { get; init; } = "";
    public string Description { get; init; } = "";
    public string Status { get; init; } = "";
    public string? AdminNote { get; init; }
    public string? RideStatus { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ReviewedAt { get; init; }

    public static RideReportViewModel From(RideReport r) => new()
    {
        Id = r.Id.ToString(),
        RidePostId = r.RidePostId?.ToString(),
        RideSummary = r.RidePost is { } p ? $"{p.Source} → {p.Destination}" : null,
        RideStatus = r.RidePost?.Status,
        ReporterId = r.ReporterId.ToString(),
        ReporterName = r.Reporter?.Name ?? "",
        ReportedUserId = r.ReportedUserId?.ToString(),
        ReportedUserName = r.ReportedUser?.Name,
        Reason = r.Reason,
        Description = r.Description,
        Status = r.Status,
        AdminNote = r.AdminNote,
        CreatedAt = r.CreatedAt,
        ReviewedAt = r.ReviewedAt,
    };
}
