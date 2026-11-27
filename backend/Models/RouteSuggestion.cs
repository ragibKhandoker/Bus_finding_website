using System.ComponentModel.DataAnnotations;

namespace SmartCommute.Server.Models;

// F20: commuters suggest a new bus route, a new stop on a route, or a correction to a route or stop. A suggestion
// never changes the buses or bus_stops tables by itself; only an admin's approval does, in one transaction.

public static class RouteSuggestionTypes
{
    public const string NewRoute = "NEW_ROUTE";
    public const string NewStop = "NEW_STOP";
    public const string IncorrectInformation = "INCORRECT_INFORMATION";

    public static readonly string[] All = [NewRoute, NewStop, IncorrectInformation];
}

public static class RouteSuggestionStatus
{
    public const string Pending = "PENDING";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";

    public static readonly string[] All = [Pending, Approved, Rejected];
}

// What an incorrect-information report asks to change. Each one maps to exactly one field of a Bus or BusStop row,
// so an approval knows precisely what to update.
public static class RouteCorrectionFields
{
    public const string BusName = "BUS_NAME";
    public const string RouteNumber = "ROUTE_NUMBER";
    public const string BusType = "BUS_TYPE";
    public const string StopName = "STOP_NAME";
    public const string StopLocation = "STOP_LOCATION";

    public static readonly string[] All = [BusName, RouteNumber, BusType, StopName, StopLocation];
    public static readonly string[] ForStop = [StopName, StopLocation];
}

// The bus types the buses table uses (see SeedData), with the fares a new route of that type starts with.
public static class RouteBusTypes
{
    public const string General = "general";
    public const string Seating = "seating";

    public static readonly string[] All = [General, Seating];

    public static (decimal BaseFare, decimal PerKmFare) StartingFares(string busType) =>
        busType == Seating ? (15m, 2.60m) : (10m, 2.45m);
}

public class RouteSuggestion
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(30)]
    public string Type { get; set; } = "";

    [MaxLength(150)]
    public string Title { get; set; } = "";

    [MaxLength(2000)]
    public string Description { get; set; } = "";

    // The existing route a NEW_STOP or INCORRECT_INFORMATION suggestion is about, and the stop a stop correction is
    // about. Set to null if that row is later removed; the names below keep what the user saw.
    public Guid? RelatedBusId { get; set; }
    public Bus? RelatedBus { get; set; }

    public Guid? RelatedBusStopId { get; set; }
    public BusStop? RelatedBusStop { get; set; }

    [MaxLength(100)]
    public string? RelatedBusName { get; set; }

    [MaxLength(30)]
    public string? RelatedRouteNumber { get; set; }

    [MaxLength(100)]
    public string? RelatedStopName { get; set; }

    // NEW_ROUTE: the bus name, its number (optional) and type. Its stops, start to end, are in Stops.
    [MaxLength(100)]
    public string? RouteName { get; set; }

    [MaxLength(20)]
    public string? RouteNumber { get; set; }

    [MaxLength(20)]
    public string? BusType { get; set; }

    // NEW_STOP: the stop's name; its position is Latitude/Longitude.
    [MaxLength(100)]
    public string? BusStopName { get; set; }

    // Where the place is, in words, e.g. "In front of Farmgate footbridge". Required for a new stop and for a stop
    // location correction, which users describe instead of giving coordinates.
    [MaxLength(200)]
    public string? LocationName { get; set; }

    // Coordinates are optional when sending (API clients may give them); the approving admin enters them when missing.
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // INCORRECT_INFORMATION: which field is wrong (RouteCorrectionFields) and the correct value. A stop location
    // correction uses Latitude/Longitude instead of CorrectedValue.
    [MaxLength(20)]
    public string? CorrectionField { get; set; }

    [MaxLength(100)]
    public string? CorrectedValue { get; set; }

    // An optional photo, kept as a private file (RouteSuggestions:StoragePath) that only the owner and admins can fetch.
    [MaxLength(50)]
    public string? ImageKey { get; set; }

    [MaxLength(20)]
    public string? ImageContentType { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = RouteSuggestionStatus.Pending;

    [MaxLength(1000)]
    public string? AdminReviewNote { get; set; }

    public Guid? ReviewedById { get; set; }
    public User? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }

    // After approval: what was changed in the official data, and the route it was changed on (the new route for
    // NEW_ROUTE), so the user can open it.
    [MaxLength(500)]
    public string? AppliedChange { get; set; }

    public Guid? AppliedBusId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Two admins reviewing the same suggestion at once cannot both be saved.
    public uint Version { get; set; }

    public List<RouteSuggestionStop> Stops { get; set; } = [];
}

// One stop of a suggested new route, in order. Stops that already exist take their position from the bus_stops
// table when the suggestion is sent. A new place has no position until the admin who approves it enters one.
public class RouteSuggestionStop
{
    public Guid Id { get; set; }
    public Guid SuggestionId { get; set; }
    public RouteSuggestion? Suggestion { get; set; }

    public int Order { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = "";

    public double? Lat { get; set; }
    public double? Lng { get; set; }

    // True when the name matched a stop already in the database.
    public bool IsExisting { get; set; }
}
