using System.ComponentModel.DataAnnotations;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Server.ViewModels;

public class ParticipantViewModel
{
    public string UserId { get; init; } = "";
    public string UserName { get; init; } = "";
    public string Gender { get; init; } = "";
}

// What anyone may see about a ride's host: no email, phone or other private details.
public class RideHostViewModel
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Gender { get; init; } = "";
    public bool Verified { get; init; }
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
    // Counts the host.
    public int MaxParticipants { get; init; }
    public string GenderPreference { get; init; } = "";
    public double? Distance { get; init; }
    public decimal? Fare { get; init; }
    // "active" (an Open ride) or "completed".
    public string Status { get; init; } = "";
    public bool IsOpen => Status == RideStatus.Active;
    // Accepted riders, not including the host.
    public List<ParticipantViewModel> Participants { get; init; } = [];
    // People on the ride so far, counting the host, and the places still free for riders.
    public int ParticipantCount { get; init; }
    public int SeatsLeft { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }

    public static RidePostViewModel From(RidePost p) => new()
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
        },
        Source = p.Source,
        Destination = p.Destination,
        MaxParticipants = p.MaxParticipants,
        GenderPreference = p.GenderPreference,
        Distance = p.Distance,
        Fare = p.Fare,
        Status = p.Status,
        Participants = p.Requests
            .Where(r => r.Status == RideRequestStatus.Accepted)
            .Select(r => new ParticipantViewModel
            {
                UserId = r.RequesterId.ToString(),
                UserName = r.RequesterName,
                Gender = r.RequesterGender,
            }).ToList(),
        ParticipantCount = RideRequestService.AcceptedCount(p) + 1,
        SeatsLeft = RideRequestService.SeatsLeft(p),
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
        CompletedAt = p.CompletedAt,
    };
}

// The host is the signed-in user; any userId, userName or gender a client still sends is ignored.
// The service checks the fields (blank places, participant range, gender preference) and explains what is wrong.
public class CreateRidePostRequest
{
    public string? Source { get; set; }

    public string? Destination { get; set; }

    public int MaxParticipants { get; set; }

    public string? GenderPreference { get; set; }
}

public class CompleteRideRequest
{
    [Required(ErrorMessage = "Choose a ride post.")]
    public Guid? PostId { get; set; }
}

// A join request. Requester is the rider's public profile; RidePost is a summary of the ride it is for.
public class RideRequestViewModel : EntityViewModel
{
    public string RidePostId { get; init; } = "";
    public string RequesterId { get; init; } = "";
    public string RequesterName { get; init; } = "";
    public string RequesterGender { get; init; } = "";
    public RideHostViewModel Requester { get; init; } = new();
    // "pending", "accepted" or "rejected".
    public string Status { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public RidePostSummary? RidePost { get; init; }

    public static RideRequestViewModel From(RideRequest r) => new()
    {
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
        },
        Status = r.Status,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
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
    public int MaxParticipants { get; init; }
    public int ParticipantCount { get; init; }
    public int SeatsLeft { get; init; }
    public DateTime CreatedAt { get; init; }

    public static RidePostSummary From(RidePost p) => new()
    {
        Id = p.Id.ToString(),
        Source = p.Source,
        Destination = p.Destination,
        UserName = p.User?.Name ?? p.UserName,
        HostId = p.UserId.ToString(),
        Status = p.Status,
        MaxParticipants = p.MaxParticipants,
        ParticipantCount = RideRequestService.AcceptedCount(p) + 1,
        SeatsLeft = RideRequestService.SeatsLeft(p),
        CreatedAt = p.CreatedAt,
    };
}

// The requester is the signed-in user; any requesterId a client still sends is ignored.
public class SendRideRequestRequest
{
    [Required(ErrorMessage = "Choose a ride post.")]
    public Guid? RidePostId { get; set; }
}
