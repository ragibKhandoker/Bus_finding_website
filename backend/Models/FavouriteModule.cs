using System.ComponentModel.DataAnnotations;

namespace SmartCommute.Server.Models;

// The Smart Favourite Bus module (Dhaka City buses only). Favourites themselves are FavoriteBus rows (Review.cs);
// everything here builds on them or on the buses table, never on a copy of the route data.

// A user's named group of favourite buses ("Office route", "Mirpur to Motijheel").
public class FavoriteCollection
{
    public const int MaxNameLength = 60;
    public const int MaxPerUser = 30;

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(MaxNameLength)]
    public string Name { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<FavoriteCollectionBus> Buses { get; set; } = [];
}

// One favourite in one collection. It points at the FavoriteBus row, so only a favourite can be in a collection,
// and removing the favourite takes it out of every collection.
public class FavoriteCollectionBus
{
    public Guid CollectionId { get; set; }
    public FavoriteCollection? Collection { get; set; }
    public Guid FavoriteBusId { get; set; }
    public FavoriteBus? FavoriteBus { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}

// What the module last saw of a favourited bus: its fares, its stops in order and how many of its vehicles are
// active. The monitor compares the buses table with this to find real fare and route changes. One row per bus.
public class BusWatchState
{
    public Guid BusId { get; set; }
    public Bus? Bus { get; set; }
    public decimal BaseFare { get; set; }
    public decimal PerKmFare { get; set; }
    public List<string> StopNames { get; set; } = [];
    public int ActiveVehicles { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}

// A bus's fare over time: the first row is the fare when the module started watching the bus, and every later
// row is a fare change found in the buses table. Nothing here is estimated.
public class BusFareHistory
{
    public Guid Id { get; set; }
    public Guid BusId { get; set; }
    public Bus? Bus { get; set; }
    public decimal BaseFare { get; set; }
    public decimal PerKmFare { get; set; }
    public decimal? PreviousBaseFare { get; set; }
    public decimal? PreviousPerKmFare { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}

// "Tell me when seats free up on this bus between these stops." The app has no seat booking: availability is the
// free seats on the route's active vehicles, from the passenger counts riders update when they board and leave.
public class BusAvailabilityAlert
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid BusId { get; set; }
    public Bus? Bus { get; set; }

    [MaxLength(100)]
    public string FromStop { get; set; } = "";

    [MaxLength(100)]
    public string ToStop { get; set; } = "";

    public bool IsActive { get; set; } = true;

    // Free seats when last checked; null until the first check.
    public int? LastSeatsFree { get; set; }
    public DateTime? LastNotifiedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// A reminder before the user's own departure on a favourite bus, from a stop on its route. The app has no
// timetable, so the user sets the departure time (Bangladesh Standard Time); it is stored in UTC.
public class BusReminder
{
    public static readonly int[] LeadMinuteChoices = [15, 30, 60];

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid BusId { get; set; }
    public Bus? Bus { get; set; }

    [MaxLength(100)]
    public string StopName { get; set; } = "";

    public DateTime DepartureAt { get; set; }
    public int LeadMinutes { get; set; } = 15;
    public bool IsEnabled { get; set; } = true;

    // When the reminder was sent; null until then. Editing the time sends it again.
    public DateTime? SentAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// A Dhaka City bus the user opened. One row per user and bus (viewing again moves it to the top); only the 10
// most recent are kept.
public class RecentlyViewedBus
{
    public const int Keep = 10;

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public Guid BusId { get; set; }
    public Bus? Bus { get; set; }
    public DateTime ViewedAt { get; set; } = DateTime.UtcNow;
}
