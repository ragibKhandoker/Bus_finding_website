using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Tests;

// Runs the signed-in ride share queries against a database that cannot be reached. EF Core checks a query (its
// translation, and include cycles in no-tracking queries) before it connects, so a broken query fails with its own
// error while a good one only fails on the connection. This caught "The Include path 'RidePost->Requests' results
// in a cycle", which broke My Rides and approving or cancelling requests.
public class RideQueryShapeTests
{
    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1")
        .UseSnakeCaseNamingConvention().Options);

    private static RideShareService Rides(AppDbContext db) =>
        new(db, new WalletService(db, Options.Create(new RewardOptions())), new NotificationService(db), Options.Create(new RideShareOptions()));

    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Post = Guid.NewGuid();

    public static TheoryData<string> Queries => new()
    {
        "hosted", "find", "stats", "search", "views", "joined", "passengers", "requests", "reload-after-change",
        "messages", "reviews", "profile", "blocks", "reports", "lifecycle",
    };

    private static Task Run(string name, AppDbContext db) => name switch
    {
        "hosted" => Rides(db).HostedAsync(Me),
        "find" => Rides(db).FindAsync(Post),
        "stats" => Rides(db).StatsAsync([Me]),
        "search" => Rides(db).Search(new RideSearch(ViewerId: Me, MinSeats: 1, Sort: RideSorts.Seats), DateTime.UtcNow).Take(12).ToListAsync(),
        "views" => Rides(db).ViewsAsync([new RidePost { Id = Post, UserId = Guid.NewGuid(), ContactPreference = RideContactPreferences.Both }], Me),
        "joined" => new RideRequestService(db, new NotificationService(db)).ForRequesterAsync(Me),
        "passengers" => new RideRequestService(db, new NotificationService(db)).PassengersAsync(Me, Post),
        "requests" => new RideRequestService(db, new NotificationService(db)).ForRideAsync(Me, Post),
        // The same shape RideRequestService reloads a request with after approving, rejecting or cancelling it.
        "reload-after-change" => db.RideRequests.Include(r => r.Requester).Include(r => r.RidePost).ThenInclude(p => p!.Requests)
            .AsNoTrackingWithIdentityResolution().SingleAsync(r => r.Id == Post),
        "messages" => new RideCommunityService(db, new NotificationService(db), Rides(db)).MessagesAsync(Me, Post, DateTime.UtcNow),
        "reviews" => new RideCommunityService(db, new NotificationService(db), Rides(db)).ReviewsAsync(Me, Post),
        "profile" => new RideCommunityService(db, new NotificationService(db), Rides(db)).ProfileAsync(Me, Me),
        "blocks" => new RideCommunityService(db, new NotificationService(db), Rides(db)).BlocksAsync(Me),
        "reports" => new RideCommunityService(db, new NotificationService(db), Rides(db)).ReportsAsync(null),
        "lifecycle" => new RideLifecycle(db, new NotificationService(db)).RunOnceAsync(DateTime.UtcNow),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(Queries))]
    public async Task Query_is_valid_and_only_fails_on_the_missing_database(string name)
    {
        await using var db = Db();
        var error = await Record.ExceptionAsync(() => Run(name, db));
        Assert.NotNull(error);
        Assert.True(error is not InvalidOperationException || error.Message.Contains("transient failure"), $"{name}: {error.Message}");
    }
}
