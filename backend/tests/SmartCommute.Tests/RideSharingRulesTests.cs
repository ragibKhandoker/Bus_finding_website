using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Tests;

// The advanced ride share rules, without a database: field checks, the discovery query's SQL and who sees what.
public class RideSharingRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 6, 0, 0, DateTimeKind.Utc); // 12:00 in Dhaka
    private static readonly RidePlace Mirpur = new("Mirpur 10", 23.8069, 90.3687);
    private static readonly RidePlace Motijheel = new("Motijheel", 23.7330, 90.4172);
    private static readonly RidePlace Savar = new("Savar", 23.8583, 90.2667);

    private static RideInput Input(string? source = "Mirpur 10", string? destination = "Motijheel", string? date = "2026-11-09",
        string? time = "18:30", int? max = 4, string? gender = "Any", string? vehicle = "Car", decimal? cost = 80,
        string? contact = null, string? instructions = null, string? pickup = null) =>
        new(source, null, null, destination, null, null, date, time, null, max, gender, vehicle, cost, instructions, contact, pickup);

    private static (RideFields? Fields, string? Error) Check(RideInput input, RidePlace? source = null, RidePlace? destination = null, int configuredMax = 10) =>
        RideRules.Check(input, source ?? Mirpur, destination ?? Motijheel, Now, configuredMax, DhakaCityArea.Default);

    [Fact]
    public void A_complete_Dhaka_ride_passes_and_keeps_Dhaka_time()
    {
        var (fields, error) = Check(Input(contact: "Both", instructions: "  AC car  ", pickup: " Gate 2 "));
        Assert.Null(error);
        Assert.Equal(new DateTime(2026, 11, 9, 12, 30, 0, DateTimeKind.Utc), fields!.DepartureAtUtc);
        Assert.Equal("both", fields.ContactPreference);
        Assert.Equal("AC car", fields.Instructions);
        Assert.Equal("Gate 2", fields.PickupDetails);
        Assert.Equal(RideVehicleTypes.Car, fields.VehicleType);
    }

    [Fact]
    public void Places_outside_Dhaka_City_or_unknown_places_are_refused()
    {
        Assert.Equal(RideRules.OutsideDhakaMessage, Check(Input(destination: "Savar"), destination: Savar).Error);
        Assert.Contains("suggestions", RideRules.Check(Input(), null, Motijheel, Now, 10, DhakaCityArea.Default).Error);
    }

    [Fact]
    public void Source_and_destination_cannot_be_the_same_place()
    {
        Assert.Contains("same place", Check(Input(destination: "mirpur 10")).Error);
        Assert.Contains("same place", Check(Input(destination: "Mirpur 10 gate"), destination: Mirpur with { Lat = Mirpur.Lat + 0.0005 }).Error);
    }

    [Theory]
    [InlineData("2026-11-09", "10:00", "past")]
    [InlineData("2027-03-01", "10:00", "60 days")]
    [InlineData("2026-11-31", "10:00", "date and time")]
    [InlineData(null, "10:00", "date and time")]
    [InlineData("2026-11-09", null, "date and time")]
    public void Departure_must_be_a_real_future_time(string? date, string? time, string expected) =>
        Assert.Contains(expected, Check(Input(date: date, time: time)).Error);

    [Theory]
    [InlineData(1, "Car", "from 2 to 10")]
    [InlineData(11, "Car", "from 2 to 10")]
    [InlineData(null, "Car", "from 2 to 10")]
    [InlineData(3, "Bike", "bike takes one rider")]
    [InlineData(5, "CNG", "at most 4")]
    [InlineData(4, "Boat", "Car, Bike, CNG or Other")]
    public void Participants_must_fit_the_vehicle(int? max, string vehicle, string expected) =>
        Assert.Contains(expected, Check(Input(max: max, vehicle: vehicle)).Error);

    [Theory]
    [InlineData(-1, "negative")]
    [InlineData(10001, "10,000")]
    [InlineData(12.345, "2 decimals")]
    public void Cost_must_be_a_sensible_amount(double cost, string expected) =>
        Assert.Contains(expected, Check(Input(cost: (decimal)cost)).Error);

    [Fact]
    public void Cost_is_required_but_zero_is_allowed()
    {
        Assert.Contains("cost per person", Check(Input(cost: null)).Error);
        Assert.Null(Check(Input(cost: 0)).Error);
    }

    [Fact]
    public void Other_fields_are_checked()
    {
        Assert.Contains("Any, Male or Female", Check(Input(gender: "Other")).Error);
        Assert.Contains("chat, phone or both", Check(Input(contact: "email")).Error);
        Assert.Contains("500", Check(Input(instructions: new string('a', 501))).Error);
        Assert.Contains("300", Check(Input(pickup: new string('a', 301))).Error);
        Assert.Contains("starts", Check(Input(source: "  ")).Error);
        Assert.Contains("goes", Check(Input(destination: "")).Error);
    }

    // ---- Discovery SQL ----

    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
        .UseSnakeCaseNamingConvention()
        .Options);

    private static RideShareService Service(AppDbContext db) => new(db,
        new WalletService(db, Options.Create(new RewardOptions())), new NotificationService(db), Options.Create(new RideShareOptions()));

    [Fact]
    public void Discovery_shows_only_open_rides_that_have_not_departed()
    {
        using var db = Db();
        var sql = Service(db).Search(new RideSearch(), Now).ToQueryString();
        Assert.Contains("r.status = 'active'", sql);
        Assert.Contains("r.deleted_at IS NULL", sql);
        Assert.Contains("r.departure_at IS NULL OR r.departure_at >", sql);
        Assert.Contains("ORDER BY r.created_at DESC", sql);
    }

    [Fact]
    public void Discovery_filters_combine()
    {
        using var db = Db();
        var sql = Service(db).Search(new RideSearch("Mirpur", "Motijheel", new DateOnly(2026, 11, 10), "Female", "CNG", 2, 50, 150,
            RideSorts.Cost, Guid.NewGuid()), Now).ToQueryString();
        Assert.Contains("r.source ILIKE", sql);
        Assert.Contains("r.destination ILIKE", sql);
        Assert.Contains("r.departure_at >=", sql);
        Assert.Contains("r.gender_preference =", sql);
        Assert.Contains("r.vehicle_type =", sql);
        Assert.Contains("(r.max_participants - 1) - (", sql);
        Assert.Contains("r.cost_per_person >=", sql);
        Assert.Contains("r.cost_per_person <=", sql);
        Assert.Contains("user_blocks", sql);
        Assert.Contains("ORDER BY r.cost_per_person IS NULL, r.cost_per_person", sql);
    }

    [Theory]
    [InlineData(RideSorts.Departure, "ORDER BY r.departure_at IS NULL, r.departure_at")]
    [InlineData(RideSorts.Seats, "ORDER BY (r.max_participants - 1) - (")]
    [InlineData(RideSorts.Recent, "ORDER BY r.created_at DESC")]
    public void Discovery_sorts(string sort, string expected)
    {
        using var db = Db();
        var sql = Service(db).Search(new RideSearch(Sort: sort), Now).ToQueryString();
        Assert.True(sql.Contains(expected), sql);
    }

    // ---- Lifecycle and privacy ----

    [Theory]
    [InlineData(RideStatus.Active, "OPEN")]
    [InlineData(RideStatus.Full, "FULL")]
    [InlineData(RideStatus.Expired, "EXPIRED")]
    public void Statuses_have_product_names(string status, string label) => Assert.Equal(label, RideStatus.Label(status));

    private static RidePost Ride(string status = RideStatus.Active, int max = 3, params (Guid Id, string Status)[] requests)
    {
        var host = Guid.NewGuid();
        var post = new RidePost
        {
            Id = Guid.NewGuid(), UserId = host, UserName = "Host", Source = "Mirpur 10", Destination = "Motijheel", MaxParticipants = max,
            Status = status, DepartureAt = Now.AddHours(3), PickupDetails = "Gate 2", ContactPreference = RideContactPreferences.Both,
        };
        post.Requests = requests.Select(r => new RideRequest { Id = Guid.NewGuid(), RidePostId = post.Id, RequesterId = r.Id, Status = r.Status, RequesterName = "Rider" }).ToList();
        return post;
    }

    [Fact]
    public void Pickup_details_and_the_host_phone_are_only_for_approved_riders()
    {
        var approved = Guid.NewGuid();
        var pending = Guid.NewGuid();
        var post = Ride(requests: [(approved, RideRequestStatus.Accepted), (pending, RideRequestStatus.Pending)]);
        RideViewer As(Guid? id) => new(id, post.Requests.FirstOrDefault(r => r.RequesterId == id), RideUserStats.None, "01700000000", false, Now);

        var stranger = RidePostViewModel.From(post, As(Guid.NewGuid()));
        Assert.Null(stranger.PickupDetails);
        Assert.Null(stranger.Contact);
        Assert.False(stranger.Viewer.CanChat);

        var waiting = RidePostViewModel.From(post, As(pending));
        Assert.Null(waiting.PickupDetails);
        Assert.Null(waiting.Contact);
        Assert.Equal("pending", waiting.Viewer.Role);
        Assert.False(waiting.Viewer.CanRequest);

        var rider = RidePostViewModel.From(post, As(approved));
        Assert.Equal("Gate 2", rider.PickupDetails);
        Assert.Equal("01700000000", rider.Contact!.Phone);
        Assert.True(rider.Viewer.CanChat);
        Assert.Equal("approved", rider.Viewer.Role);

        var host = RidePostViewModel.From(post, As(post.UserId) with { MyRequest = null });
        Assert.Equal("Gate 2", host.PickupDetails);
        Assert.True(host.Viewer.CanManage);
        Assert.Equal(1, host.PendingCount);
        Assert.Null(stranger.PendingCount);
    }

    [Fact]
    public void Request_to_join_is_offered_only_on_open_rides_with_seats()
    {
        var someone = new RideViewer(Guid.NewGuid(), null, RideUserStats.None, null, false, Now);
        Assert.True(RidePostViewModel.From(Ride(), someone).Viewer.CanRequest);
        Assert.Equal("full", RidePostViewModel.From(Ride(RideStatus.Full), someone).Viewer.CannotRequestReason);
        Assert.Equal("closed", RidePostViewModel.From(Ride(RideStatus.Cancelled), someone).Viewer.CannotRequestReason);
        Assert.Equal("blocked", RidePostViewModel.From(Ride(), someone with { Blocked = true }).Viewer.CannotRequestReason);
        Assert.Equal("signin", RidePostViewModel.From(Ride(), RideViewer.Anonymous(Now)).Viewer.CannotRequestReason);
        var departed = Ride();
        departed.DepartureAt = Now.AddMinutes(-1);
        Assert.Equal("departed", RidePostViewModel.From(departed, someone).Viewer.CannotRequestReason);
        Assert.False(RidePostViewModel.From(departed, someone).IsOpen);
    }

    [Fact]
    public void Reviews_open_to_members_only_after_completion()
    {
        var rider = Guid.NewGuid();
        var done = Ride(RideStatus.Completed, requests: [(rider, RideRequestStatus.Accepted)]);
        var viewer = new RideViewer(rider, done.Requests[0], RideUserStats.None, null, false, Now);
        Assert.True(RidePostViewModel.From(done, viewer).Viewer.CanReview);
        var upcoming = Ride(requests: [(rider, RideRequestStatus.Accepted)]);
        Assert.False(RidePostViewModel.From(upcoming, viewer with { MyRequest = upcoming.Requests[0] }).Viewer.CanReview);
        Assert.False(RidePostViewModel.From(done, new RideViewer(Guid.NewGuid(), null, RideUserStats.None, null, false, Now)).Viewer.CanReview);
    }

    [Theory]
    [InlineData("Bike", 10, 2)]
    [InlineData("CNG", 10, 4)]
    [InlineData("Car", 10, 10)]
    public void Vehicle_capacity(string vehicle, int configured, int expected) => Assert.Equal(expected, RideVehicleTypes.Capacity(vehicle, configured));
}
