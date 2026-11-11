using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Tests;

// F22: trip and status rules and query shapes, without a database.
public class DriverTripRuleTests
{
    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1")
        .UseSnakeCaseNamingConvention().Options);

    private static DriverTripService Service(AppDbContext db) => new(db, new NotificationService(db), Options.Create(new DriverTripOptions()));

    [Fact]
    public void On_trip_can_never_be_chosen_by_hand()
    {
        Assert.DoesNotContain(DriverWorkStatus.OnTrip, DriverWorkStatus.Manual);
        Assert.Equal([DriverWorkStatus.Available, DriverWorkStatus.OnDuty], DriverWorkStatus.CanStartTrip);
    }

    [Fact]
    public async Task Choosing_on_trip_by_hand_is_refused_before_touching_the_database()
    {
        await using var db = Db();
        var result = await Service(db).SetStatusAsync(Guid.NewGuid(), "On Trip", null);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(400, (await Service(db).SetStatusAsync(Guid.NewGuid(), "Sleeping", null)).StatusCode);
    }

    private static DriverTrip Trip(string status, DateTime departure, Guid busId) => new()
    {
        Id = Guid.NewGuid(), BusId = busId, Status = status, ScheduledDeparture = departure, ScheduledArrival = departure.AddMinutes(50),
    };

    [Fact]
    public async Task A_trip_can_be_started_only_in_its_window_with_an_active_assignment()
    {
        await using var db = Db();
        var service = Service(db);
        var bus = Guid.NewGuid();
        var departure = new DateTime(2026, 11, 12, 2, 0, 0, DateTimeKind.Utc);
        var trip = Trip(DriverTripStatus.Scheduled, departure, bus);
        Assert.False(service.StateOf(trip, [bus], departure.AddMinutes(-31)).CanStart); // too early
        Assert.True(service.StateOf(trip, [bus], departure.AddMinutes(-30)).CanStart);
        Assert.True(service.StateOf(trip, [bus], departure.AddMinutes(59)).CanStart);
        Assert.False(service.StateOf(trip, [bus], departure.AddMinutes(60)).CanStart);  // missed by then
        Assert.False(service.StateOf(trip, [], departure).CanStart);                    // assignment ended
        Assert.False(service.StateOf(Trip(DriverTripStatus.Completed, departure, bus), [bus], departure).CanStart);
        Assert.True(service.StateOf(Trip(DriverTripStatus.InProgress, departure, bus), [bus], departure).CanComplete);
    }

    [Fact]
    public void Dhaka_schedule_times_are_stored_in_utc()
    {
        Assert.Equal(new DateTime(2026, 11, 12, 2, 30, 0, DateTimeKind.Utc), DriverScheduleService.DhakaToUtc(new DateOnly(2026, 11, 12), "08:30"));
        Assert.Null(DriverScheduleService.DhakaToUtc(new DateOnly(2026, 11, 12), "8 o'clock"));
    }

    [Fact]
    public void Return_trips_run_the_stops_backwards()
    {
        var bus = new Bus { Stops = [new BusStop { Order = 0, Name = "Mirpur 10" }, new BusStop { Order = 1, Name = "Farmgate" }, new BusStop { Order = 2, Name = "Motijheel" }] };
        Assert.Equal(("Mirpur 10", "Motijheel"), DriverScheduleService.Ends(bus, DriverTripDirection.Forward));
        Assert.Equal(("Motijheel", "Mirpur 10"), DriverScheduleService.Ends(bus, DriverTripDirection.Return));
        var view = DriverTripViewModel.From(new DriverTrip { Bus = bus, Direction = DriverTripDirection.Return }, withStops: true);
        Assert.Equal(["Motijheel", "Farmgate", "Mirpur 10"], view.Stops);
    }

    public static TheoryData<string> Queries => new() { "trips", "search", "trip", "routes", "stats", "status", "history", "assignments", "admin-list", "operations", "sweep" };

    [Theory]
    [MemberData(nameof(Queries))]
    public async Task Queries_are_valid_SQL(string name)
    {
        await using var db = Db();
        var service = Service(db);
        var schedules = new DriverScheduleService(db, service, new NotificationService(db));
        var now = DateTime.UtcNow;
        Task run = name switch
        {
            "trips" => service.TripsAsync(Guid.NewGuid(), new DriverTripFilter(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 1), DriverTripStatus.Completed, Oldest: true, Page: 2), now),
            "search" => service.TripsAsync(Guid.NewGuid(), new DriverTripFilter(Search: "Mirpur", UpcomingOnly: true), now),
            "trip" => service.TripAsync(Guid.NewGuid(), Guid.NewGuid(), now),
            "routes" => service.RoutesAsync(Guid.NewGuid()),
            "stats" => service.StatsAsync(Guid.NewGuid(), now),
            "status" => service.StatusAsync(Guid.NewGuid()),
            "history" => service.StatusHistoryAsync(Guid.NewGuid()),
            "assignments" => service.AssignmentsAsync(Guid.NewGuid()),
            "admin-list" => schedules.ListAsync(new AdminScheduleFilter(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 1), null, "SCHEDULED"), now),
            "operations" => schedules.OperationsAsync(now),
            _ => service.SweepAsync(now),
        };
        var error = await Record.ExceptionAsync(() => run);
        Assert.NotNull(error);
        Assert.True(error is not InvalidOperationException || error.Message.Contains("transient failure"), $"{name}: {error.Message} | {error.InnerException?.Message}");
    }
}

// F22 end to end against PostgreSQL (skipped without SMARTCOMMUTE_TEST_DB). Times are chosen days ahead and passed to
// the services, so the tests do not depend on the clock.
[Collection(ServerCollection.Name)]
public class DriverTripFlowTests(TestServerFixture server)
{
    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    // An approved driver with an active assignment to the first route, signed in with a driver token.
    private async Task<(Guid DriverId, HttpClient Client, Guid BusId, Guid AssignmentId)> DriverAsync(bool approved = true, bool assigned = true)
    {
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<JwtTokenService>();
        var id = Guid.NewGuid().ToString("N");
        var user = new User { Name = "Trip Driver", Email = $"{id}@driver.test", FriendCode = id[..5].ToUpperInvariant(), PasswordHash = "x", Role = UserRoles.Driver };
        db.Users.Add(user);
        db.DriverProfiles.Add(new DriverProfile
        {
            User = user, DateOfBirth = new DateOnly(1990, 1, 1), LicenseNumberHash = id + "l", NidNumberHash = id + "n", LicenseType = "Professional",
            LicenseExpiryDate = new DateOnly(2032, 1, 1), DrivingExperienceYears = 5, EmploymentType = "Full-time",
            VerificationStatus = approved ? DriverVerificationStatus.Approved : DriverVerificationStatus.Pending,
            AccountStatus = approved ? DriverAccountStatus.Active : DriverAccountStatus.PendingVerification,
        });
        var bus = await db.Buses.Include(b => b.IndividualBuses).Where(b => b.RunsBothWays).OrderBy(b => b.RouteNumber).FirstAsync();
        var assignment = new DriverBusAssignment { Driver = user, BusId = bus.Id, IsActive = assigned };
        db.DriverBusAssignments.Add(assignment);
        var session = new UserSession { User = user, CreatedAt = DateTime.UtcNow, ExpiresAt = tokens.NewExpiry() };
        db.UserSessions.Add(session);
        await db.SaveChangesAsync();
        return (user.Id, server.ClientFor(tokens.CreateToken(user, session)), bus.Id, assignment.Id);
    }

    private static readonly DateOnly Day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5);
    private static DateTime At(string time) => DriverScheduleService.DhakaToUtc(Day, time)!.Value;

    private async Task<ServiceResult<DriverTrip>> ScheduleAsync(Guid driverId, Guid busId, string dep, string arr, string direction = "FORWARD", Guid? vehicle = null)
    {
        await using var scope = server.Scope();
        var (admin, _) = await server.CreateAdminAsync();
        return await scope.ServiceProvider.GetRequiredService<DriverScheduleService>().CreateAsync(admin.Id, new DriverScheduleRequest
        {
            DriverId = driverId, BusId = busId, Date = Day.ToString("yyyy-MM-dd"), DepartureTime = dep, ArrivalTime = arr, Direction = direction, IndividualBusId = vehicle,
        }, DateTime.UtcNow);
    }

    private async Task<T> With<T>(Func<DriverTripService, Task<T>> run)
    {
        await using var scope = server.Scope();
        return await run(scope.ServiceProvider.GetRequiredService<DriverTripService>());
    }

    [SkippableFact]
    public async Task Driver_runs_a_trip_from_start_to_finish()
    {
        RequireServer();
        var (driverId, client, busId, _) = await DriverAsync();
        var first = await ScheduleAsync(driverId, busId, "08:00", "09:00");
        Assert.True(first.Succeeded, first.Message);
        var second = await ScheduleAsync(driverId, busId, "10:00", "11:00", "RETURN");
        Assert.True(second.Succeeded, second.Message);
        Assert.Equal(1, first.Value!.TripNumber);
        Assert.Equal(2, second.Value!.TripNumber);
        Assert.Equal(first.Value.EndStop, second.Value.StartStop); // the return trip runs back

        // Off duty: cannot start. Available: can.
        var tripId = first.Value.Id;
        Assert.Equal(409, (await With(s => s.StartAsync(driverId, tripId, At("07:50")))).StatusCode);
        Assert.Equal(200, (await With(s => s.SetStatusAsync(driverId, "AVAILABLE", null))).StatusCode);
        Assert.Equal(409, (await With(s => s.StartAsync(driverId, tripId, At("07:00")))).StatusCode); // too early
        Assert.Equal(409, (await With(s => s.CompleteAsync(driverId, tripId, At("07:55")))).StatusCode); // not started
        var started = await With(s => s.StartAsync(driverId, tripId, At("07:55")));
        Assert.True(started.Succeeded, started.Message);
        Assert.Equal(DriverWorkStatus.OnTrip, (await With(s => s.StatusAsync(driverId))).CurrentStatus);

        // No second trip, no start again, no off duty during the trip.
        Assert.Equal(409, (await With(s => s.StartAsync(driverId, second.Value.Id, At("09:40")))).StatusCode);
        Assert.Equal(409, (await With(s => s.StartAsync(driverId, tripId, At("07:56")))).StatusCode);
        Assert.Equal(409, (await With(s => s.SetStatusAsync(driverId, "OFF_DUTY", null))).StatusCode);

        var done = await With(s => s.CompleteAsync(driverId, tripId, At("08:58")));
        Assert.True(done.Succeeded, done.Message);
        Assert.Equal(63, done.Value!.DurationMinutes);
        Assert.Equal(409, (await With(s => s.CompleteAsync(driverId, tripId, At("08:59")))).StatusCode); // duplicate request
        Assert.Equal(409, (await With(s => s.StartAsync(driverId, tripId, At("09:00")))).StatusCode);    // completed trips do not restart

        await using (var scope = server.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.Notifications.AnyAsync(n => n.UserId == driverId && n.Title == "Trip completed successfully!"));
            Assert.True(await db.Notifications.AnyAsync(n => n.UserId == driverId && n.Title == "New trip scheduled"));
            var history = await db.DriverStatusHistory.Where(h => h.DriverId == driverId).OrderBy(h => h.ChangedAt).Select(h => h.NewStatus).ToListAsync();
            Assert.Equal([DriverWorkStatus.Available, DriverWorkStatus.OnTrip, DriverWorkStatus.Available], history);
        }

        // The completed trip is counted once; the dashboard shows it through the API.
        var stats = await With(s => s.StatsAsync(driverId, At("12:00")));
        Assert.Equal(1, stats.Completed);
        Assert.Equal(2, stats.TotalAssigned);
        var dash = await JsonOf(await client.GetAsync("/api/driver/dashboard"));
        Assert.Equal(busId.ToString(), dash.GetProperty("data").GetProperty("assignments")[0].GetProperty("route").GetProperty("id").GetString());
        Assert.Equal(1, dash.GetProperty("data").GetProperty("completed").GetProperty("total").GetInt32());
        var detail = await client.GetAsync($"/api/driver/trips/{tripId}");
        Assert.Equal("COMPLETED", (await JsonOf(detail)).GetProperty("data").GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task Cancelled_and_missed_trips_are_not_completed_trips()
    {
        RequireServer();
        var (driverId, _, busId, _) = await DriverAsync();
        var trip = (await ScheduleAsync(driverId, busId, "13:00", "14:00")).Value!;
        var other = (await ScheduleAsync(driverId, busId, "15:00", "16:00")).Value!;
        await using (var scope = server.Scope())
        {
            var (admin, _) = await server.CreateAdminAsync();
            var schedules = scope.ServiceProvider.GetRequiredService<DriverScheduleService>();
            Assert.Equal(400, (await schedules.CancelAsync(admin.Id, trip.Id, "")).StatusCode);
            Assert.True((await schedules.CancelAsync(admin.Id, trip.Id, "Road closed")).Succeeded);
        }
        Assert.Equal(409, (await With(s => s.StartAsync(driverId, trip.Id, At("12:55")))).StatusCode);
        await With(s => s.SweepAsync(At("16:10"))); // an hour past 15:00 and never started
        var stats = await With(s => s.StatsAsync(driverId, At("17:00")));
        Assert.Equal(0, stats.Completed);
        Assert.Equal(1, stats.Cancelled);
        Assert.Equal(1, stats.Missed);
        Assert.Equal(0.0, stats.CompletionRate);
        Assert.Equal(DriverTripStatus.Missed, (await With(s => s.TripAsync(driverId, other.Id, At("17:00"))))!.Status);
    }

    [SkippableFact]
    public async Task Schedules_must_not_overlap_and_need_an_active_assignment()
    {
        RequireServer();
        var (driverId, _, busId, assignmentId) = await DriverAsync();
        Assert.True((await ScheduleAsync(driverId, busId, "06:00", "07:00")).Succeeded);
        Assert.Equal(409, (await ScheduleAsync(driverId, busId, "06:30", "07:30")).StatusCode);  // overlaps
        Assert.Equal(400, (await ScheduleAsync(driverId, busId, "09:00", "08:00")).StatusCode);  // arrival before departure

        Guid vehicle;
        await using (var scope = server.Scope())
        {
            vehicle = await scope.ServiceProvider.GetRequiredService<AppDbContext>().IndividualBuses.Where(v => v.BusId == busId).Select(v => v.Id).FirstAsync();
        }
        var (otherDriver, _, _, _) = await DriverAsync();
        Assert.True((await ScheduleAsync(driverId, busId, "18:00", "19:00", vehicle: vehicle)).Succeeded);
        Assert.Equal(409, (await ScheduleAsync(otherDriver, busId, "18:30", "19:30", vehicle: vehicle)).StatusCode); // vehicle busy

        var (pending, _, pendingBus, _) = await DriverAsync(approved: false);
        Assert.Equal(409, (await ScheduleAsync(pending, pendingBus, "06:00", "07:00")).StatusCode);
        var (unassigned, _, otherBus, _) = await DriverAsync(assigned: false);
        Assert.Equal(409, (await ScheduleAsync(unassigned, otherBus, "06:00", "07:00")).StatusCode);

        // Ending the assignment cancels future trips and blocks starting them.
        var later = (await ScheduleAsync(driverId, busId, "20:00", "21:00")).Value!;
        await using (var scope = server.Scope())
        {
            var (admin, _) = await server.CreateAdminAsync();
            Assert.True((await scope.ServiceProvider.GetRequiredService<DriverVerificationService>().EndAssignmentAsync(admin.Id, driverId, assignmentId)).Succeeded);
        }
        Assert.Equal(DriverTripStatus.Cancelled, (await With(s => s.TripAsync(driverId, later.Id, DateTime.UtcNow)))!.Status);
    }

    [SkippableFact]
    public async Task Drivers_only_see_and_change_their_own_trips()
    {
        RequireServer();
        var (ownerId, owner, busId, _) = await DriverAsync();
        var (intruderId, intruder, _, _) = await DriverAsync();
        var trip = (await ScheduleAsync(ownerId, busId, "11:00", "12:00")).Value!;

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/driver/trips/{trip.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/api/driver/trips/{trip.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/api/driver/schedule/{trip.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.PostAsync($"/api/driver/trips/{trip.Id}/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.PostAsync($"/api/driver/trips/{trip.Id}/complete", null)).StatusCode);
        Assert.Equal(404, (await With(s => s.StartAsync(intruderId, trip.Id, trip.ScheduledDeparture))).StatusCode);
        Assert.Empty((await JsonOf(await intruder.GetAsync("/api/driver/trips"))).GetProperty("data").EnumerateArray());

        // Drivers cannot schedule, assign or reach admin endpoints; commuters and pending drivers cannot reach driver ones.
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/admin/driver-schedules", new { driverId = ownerId, busId })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/admin/driver-assignments", new { driverId = ownerId, busId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/driver/working-status", new { status = "ON_TRIP" })).StatusCode);
        var (_, commuter) = await server.CreateUserAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await commuter.GetAsync("/api/driver/dashboard")).StatusCode);
        var (_, pending, _, _) = await DriverAsync(approved: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await pending.GetAsync("/api/driver/trips")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.ClientFor(null).GetAsync("/api/driver/dashboard")).StatusCode);
    }

    [SkippableFact]
    public async Task Two_starts_at_once_only_one_wins()
    {
        RequireServer();
        var (driverId, _, busId, _) = await DriverAsync();
        var trip = (await ScheduleAsync(driverId, busId, "22:00", "22:45")).Value!;
        await With(s => s.SetStatusAsync(driverId, "ON_DUTY", null));
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => With(s => s.StartAsync(driverId, trip.Id, At("21:50")))));
        Assert.Single(results, r => r.Succeeded);
        Assert.All(results.Where(r => !r.Succeeded), r => Assert.Equal(409, r.StatusCode));
    }

    [SkippableFact]
    public async Task Admin_manages_schedules_through_the_api()
    {
        RequireServer();
        var (driverId, driver, busId, _) = await DriverAsync();
        var (_, admin) = await server.CreateAdminAsync();
        var created = await admin.PostAsJsonAsync("/api/admin/driver-schedules", new
        {
            driverId, busId, date = Day.ToString("yyyy-MM-dd"), departureTime = "07:15", arrivalTime = "08:05", direction = "FORWARD",
        });
        var body = await JsonOf(created);
        Assert.True(created.StatusCode == HttpStatusCode.Created, body.ToString());
        var id = body.GetProperty("data").GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/driver-schedules/{id}",
            new { date = Day.ToString("yyyy-MM-dd"), departureTime = "07:30", arrivalTime = "08:20" })).StatusCode);
        var list = await JsonOf(await admin.GetAsync($"/api/admin/driver-schedules?driverId={driverId}"));
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        var ops = await JsonOf(await admin.GetAsync("/api/admin/driver-operations"));
        Assert.Contains(ops.GetProperty("data").EnumerateArray(), row => row.GetProperty("driverId").GetString() == driverId.ToString());
        var cancel = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/driver-schedules/{id}") { Content = JsonContent.Create(new { reason = "Bus in workshop" }) };
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(cancel)).StatusCode);
        var upcoming = await JsonOf(await driver.GetAsync("/api/driver/trips/upcoming"));
        Assert.Empty(upcoming.GetProperty("data").EnumerateArray());
    }
}
