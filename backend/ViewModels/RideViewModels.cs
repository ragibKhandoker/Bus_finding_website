using System.ComponentModel.DataAnnotations;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.ViewModels;

public class ParticipantViewModel
{
    public string UserId { get; init; } = "";
    public string UserName { get; init; } = "";
    public string Gender { get; init; } = "";
}

public class RidePostViewModel : EntityViewModel
{
    public string UserId { get; init; } = "";
    public string UserName { get; init; } = "";
    public string Gender { get; init; } = "";
    public string Source { get; init; } = "";
    public string Destination { get; init; } = "";
    public int MaxParticipants { get; init; }
    public double? Distance { get; init; }
    public decimal? Fare { get; init; }
    public string Status { get; init; } = "";
    // Accepted riders, not including the host.
    public List<ParticipantViewModel> Participants { get; init; } = [];
    public DateTime CreatedAt { get; init; }

    public static RidePostViewModel From(RidePost p) => new()
    {
        Id = p.Id.ToString(),
        UserId = p.UserId.ToString(),
        UserName = p.UserName,
        Gender = p.Gender,
        Source = p.Source,
        Destination = p.Destination,
        MaxParticipants = p.MaxParticipants,
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
        CreatedAt = p.CreatedAt,
    };
}

public class CreateRidePostRequest
{
    [Required]
    public Guid? UserId { get; set; }

    [StringLength(100)]
    public string? UserName { get; set; }

    [StringLength(30)]
    public string? Gender { get; set; }

    [Required, StringLength(200, MinimumLength = 1)]
    public string Source { get; set; } = "";

    [Required, StringLength(200, MinimumLength = 1)]
    public string Destination { get; set; } = "";

    [Range(2, 10)]
    public int MaxParticipants { get; set; } = 4;

    [Range(0, 1000)]
    public double? Distance { get; set; }

    [Range(0, 100_000)]
    public decimal? Fare { get; set; }
}

public class CompleteRideRequest
{
    [Required]
    public Guid? PostId { get; set; }

    public Guid? UserId { get; set; }
}

public class RideRequestViewModel : EntityViewModel
{
    public string RidePostId { get; init; } = "";
    public string RequesterId { get; init; } = "";
    public string RequesterName { get; init; } = "";
    public string RequesterGender { get; init; } = "";
    public string Status { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public RidePostSummary? RidePost { get; init; }

    public static RideRequestViewModel From(RideRequest r) => new()
    {
        Id = r.Id.ToString(),
        RidePostId = r.RidePostId.ToString(),
        RequesterId = r.RequesterId.ToString(),
        RequesterName = r.RequesterName,
        RequesterGender = r.RequesterGender,
        Status = r.Status,
        CreatedAt = r.CreatedAt,
        RidePost = r.RidePost is null ? null : new RidePostSummary
        {
            Id = r.RidePost.Id.ToString(),
            Source = r.RidePost.Source,
            Destination = r.RidePost.Destination,
            UserName = r.RidePost.UserName,
            Status = r.RidePost.Status,
        },
    };
}

public class RidePostSummary : EntityViewModel
{
    public string Source { get; init; } = "";
    public string Destination { get; init; } = "";
    public string UserName { get; init; } = "";
    public string Status { get; init; } = "";
}

public class SendRideRequestRequest
{
    [Required]
    public Guid? RidePostId { get; set; }

    [Required]
    public Guid? RequesterId { get; set; }

    [StringLength(100)]
    public string? RequesterName { get; set; }

    [StringLength(30)]
    public string? RequesterGender { get; set; }
}
