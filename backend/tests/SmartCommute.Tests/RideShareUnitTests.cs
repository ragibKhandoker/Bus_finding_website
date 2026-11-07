using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Tests;

// Runs without a database: ToQueryString shows the SQL without connecting.
public class RideShareUnitTests
{
    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
        .UseSnakeCaseNamingConvention()
        .Options);

    private static RideShareService Service(AppDbContext db) => new(db,
        new WalletService(db, Options.Create(new RewardOptions())), new NotificationService(db), Options.Create(new RideShareOptions()));

    [Fact]
    public void Open_rides_filter_by_destination_without_case_and_skip_deleted_and_completed()
    {
        using var db = Db();
        var sql = Service(db).Open("  Dhanmondi ").ToQueryString();
        Assert.Contains("r.destination ILIKE @", sql);
        Assert.Contains("'%Dhanmondi%'", sql);
        Assert.Contains("r.deleted_at IS NULL", sql);
        Assert.Contains("r.status = 'active'", sql);
        Assert.Contains("ORDER BY r.created_at DESC", sql);
    }

    [Fact]
    public void Destination_wildcards_are_matched_literally()
    {
        using var db = Db();
        Assert.Contains(@"'%100\%%'", Service(db).Open("100%").ToQueryString());
    }

    [Theory]
    [InlineData(2, 0, 1)]
    [InlineData(2, 1, 0)]
    [InlineData(4, 2, 1)]
    [InlineData(4, 5, 0)]
    public void Seats_left_count_the_host_and_only_accepted_riders(int maxParticipants, int accepted, int seatsLeft)
    {
        var post = new RidePost { MaxParticipants = maxParticipants };
        post.Requests.AddRange(Enumerable.Range(0, accepted).Select(_ => new RideRequest { Status = RideRequestStatus.Accepted }));
        post.Requests.Add(new RideRequest { Status = RideRequestStatus.Pending });
        post.Requests.Add(new RideRequest { Status = RideRequestStatus.Rejected });
        Assert.Equal(seatsLeft, RideRequestService.SeatsLeft(post));
        Assert.Equal(accepted, RideRequestService.AcceptedCount(post));
    }

    [Theory]
    [InlineData("any", "Any")]
    [InlineData(" FEMALE ", "Female")]
    [InlineData("Male", "Male")]
    [InlineData("Other", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Gender_preference_accepts_only_the_listed_values(string? input, string? expected)
    {
        Assert.Equal(expected, RideGenderPreference.Normalize(input));
    }

    [Theory]
    [InlineData("Any", "Female", true)]
    [InlineData("Female", "Female", true)]
    [InlineData("Female", "Male", false)]
    [InlineData("Male", "Prefer not to say", false)]
    public void Gender_preference_decides_who_may_join(string preference, string gender, bool allowed)
    {
        Assert.Equal(allowed, RideGenderPreference.Allows(preference, gender));
    }
}
