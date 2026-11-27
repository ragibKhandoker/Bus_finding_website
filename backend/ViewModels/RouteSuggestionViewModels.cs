using System.ComponentModel.DataAnnotations;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.ViewModels;

// F20: what the route & stop suggestion pages send and read. The submitter always comes from the sign-in token,
// and the status from the server; neither is read from a request.

public class SuggestionStopRequest
{
    [StringLength(100)]
    public string Name { get; set; } = "";

    // Needed only for a place that is not already a bus stop in the database.
    public double? Lat { get; set; }
    public double? Lng { get; set; }
}

public class CreateRouteSuggestionRequest
{
    [Required(ErrorMessage = "Choose what you are suggesting."), StringLength(30)]
    public string Type { get; set; } = "";

    [Required(ErrorMessage = "Enter a short title."), StringLength(150, ErrorMessage = "The title can be at most 150 characters.")]
    public string Title { get; set; } = "";

    [Required(ErrorMessage = "Describe your suggestion."), StringLength(2000, ErrorMessage = "The description can be at most 2000 characters.")]
    public string Description { get; set; } = "";

    public Guid? RelatedBusId { get; set; }
    public Guid? RelatedBusStopId { get; set; }

    [StringLength(100)] public string? RouteName { get; set; }
    [StringLength(20)] public string? RouteNumber { get; set; }
    [StringLength(20)] public string? BusType { get; set; }

    // NEW_ROUTE: every stop from start to end, in order.
    public List<SuggestionStopRequest>? Stops { get; set; }

    [StringLength(100)] public string? BusStopName { get; set; }
    [StringLength(200)] public string? LocationName { get; set; }

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    [StringLength(20)] public string? CorrectionField { get; set; }
    [StringLength(100)] public string? CorrectedValue { get; set; }

    // An optional photo as a data URL (JPG, PNG or WEBP).
    public string? Image { get; set; }

    // When editing: true removes the photo already attached (a new Image replaces it anyway).
    public bool RemoveImage { get; set; }
}

// A position the approving admin enters for a place the user only described.
public class ReviewStopLocation
{
    public int Order { get; set; }
    public double? Lat { get; set; }
    public double? Lng { get; set; }
}

public class ReviewRouteSuggestionRequest
{
    [Required(ErrorMessage = "Choose APPROVED or REJECTED."), StringLength(20)]
    public string Status { get; set; } = "";

    [StringLength(1000, ErrorMessage = "The note can be at most 1000 characters.")]
    public string? AdminReviewNote { get; set; }

    // When approving: the position of a new stop or a moved stop, if the suggestion has none.
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // When approving a new route: the positions of its new places, by stop order.
    public List<ReviewStopLocation>? StopLocations { get; set; }
}

public class SuggestionStopViewModel
{
    public int Order { get; init; }
    public string Name { get; init; } = "";
    public double? Lat { get; init; }
    public double? Lng { get; init; }
    public bool IsExisting { get; init; }
}

// A place whose coordinates the admin has to enter before approving. Order is the stop's order for a new route,
// null for the suggestion's own location.
public class NeededLocationViewModel
{
    public int? Order { get; init; }
    public string Name { get; init; } = "";
    // What the user wrote about where it is.
    public string? Description { get; init; }
}

public class SuggestionUserViewModel
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
    public string? Phone { get; init; }
}

public class RouteSuggestionViewModel
{
    public string Id { get; init; } = "";
    // A short reference for people to quote, e.g. "RS-3F2A9C1B".
    public string Code { get; init; } = "";
    public string Type { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string? RelatedBusId { get; init; }
    public string? RelatedBusName { get; init; }
    public string? RelatedRouteNumber { get; init; }
    public string? RelatedBusStopId { get; init; }
    public string? RelatedStopName { get; init; }
    public string? RouteName { get; init; }
    public string? RouteNumber { get; init; }
    public string? BusType { get; init; }
    public string? StartLocation { get; init; }
    public string? EndLocation { get; init; }
    public List<SuggestionStopViewModel> Stops { get; init; } = [];
    public string? BusStopName { get; init; }
    public string? LocationName { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public string? CorrectionField { get; init; }
    public string? CorrectedValue { get; init; }
    // Needs the sign-in token: the page fetches it and shows it as a blob.
    public string? ImageUrl { get; init; }
    public string Status { get; init; } = "";
    public string? AdminReviewNote { get; init; }
    public DateTime? ReviewedAt { get; init; }
    public string? AppliedChange { get; init; }
    public string? AppliedBusId { get; init; }
    public bool CanDelete { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    // Only for admins.
    public SuggestionUserViewModel? User { get; init; }
    public string? ReviewedByName { get; init; }

    public static string CodeOf(Guid id) => "RS-" + id.ToString("N")[..8].ToUpperInvariant();

    public static RouteSuggestionViewModel From(RouteSuggestion s, Guid viewerId, bool forAdmin)
    {
        var stops = s.Stops.OrderBy(x => x.Order).ToList();
        return new()
        {
            Id = s.Id.ToString(), Code = CodeOf(s.Id), Type = s.Type, Title = s.Title, Description = s.Description,
            RelatedBusId = s.RelatedBusId?.ToString(), RelatedBusName = s.RelatedBusName, RelatedRouteNumber = s.RelatedRouteNumber,
            RelatedBusStopId = s.RelatedBusStopId?.ToString(), RelatedStopName = s.RelatedStopName,
            RouteName = s.RouteName, RouteNumber = s.RouteNumber, BusType = s.BusType,
            StartLocation = stops.FirstOrDefault()?.Name, EndLocation = stops.Count > 1 ? stops[^1].Name : null,
            Stops = stops.Select(x => new SuggestionStopViewModel { Order = x.Order, Name = x.Name, Lat = x.Lat, Lng = x.Lng, IsExisting = x.IsExisting }).ToList(),
            BusStopName = s.BusStopName, LocationName = s.LocationName, Latitude = s.Latitude, Longitude = s.Longitude,
            CorrectionField = s.CorrectionField, CorrectedValue = s.CorrectedValue,
            ImageUrl = s.ImageKey is null ? null : $"/api/route-suggestions/{s.Id}/image",
            Status = s.Status, AdminReviewNote = s.AdminReviewNote, ReviewedAt = s.ReviewedAt, AppliedChange = s.AppliedChange,
            AppliedBusId = s.AppliedBusId?.ToString(),
            CanDelete = s.Status == RouteSuggestionStatus.Pending && s.UserId == viewerId || forAdmin,
            CreatedAt = s.CreatedAt, UpdatedAt = s.UpdatedAt,
            User = forAdmin && s.User is { } u ? new SuggestionUserViewModel { Id = u.Id.ToString(), Name = u.Name, Email = u.Email, Phone = u.Phone } : null,
            ReviewedByName = forAdmin ? s.ReviewedBy?.Name : null,
        };
    }
}

// What approving a suggestion would do to the official data, shown to the admin before they confirm.
public class SuggestionApprovalPreviewViewModel
{
    public bool CanApprove { get; init; }
    // Why it cannot be approved as it stands.
    public string? Problem { get; init; }
    public List<string> Changes { get; init; } = [];
    // Places the admin must give coordinates for when approving.
    public List<NeededLocationViewModel> NeedsLocations { get; init; } = [];
}
