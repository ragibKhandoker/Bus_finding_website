using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartCommute.Server.Data;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Tests;

// F19: the travel preference field rules, without a database.
public class TravelPreferenceRuleTests
{
    private static TravelPreferenceRequest Valid() => new()
    {
        PreferenceName = "University Journey", StartLocation = "Mirpur 10", Destination = "Shahbag", BusId = Guid.NewGuid(),
        PreferredTravelTime = "08:30", WeeklyFrequency = 5, TravelDays = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday"],
        BusType = "Non-AC", TravelPurpose = "University",
    };

    [Fact]
    public void A_complete_preference_passes() => Assert.Null(CommuterProfileService.CheckFields(Valid()));

    [Theory]
    [InlineData("name")]
    [InlineData("start")]
    [InlineData("destination")]
    [InlineData("same-ends")]
    [InlineData("bus")]
    [InlineData("time")]
    [InlineData("frequency-0")]
    [InlineData("frequency-8")]
    [InlineData("no-days")]
    [InlineData("bad-day")]
    [InlineData("repeated-day")]
    [InlineData("bus-type")]
    [InlineData("purpose")]
    public void Bad_fields_are_refused(string problem)
    {
        var r = Valid();
        switch (problem)
        {
            case "name": r.PreferenceName = "  "; break;
            case "start": r.StartLocation = ""; break;
            case "destination": r.Destination = " "; break;
            case "same-ends": r.Destination = "mirpur 10"; break;
            case "bus": r.BusId = Guid.Empty; break;
            case "time": r.PreferredTravelTime = "25:99"; break;
            case "frequency-0": r.WeeklyFrequency = 0; break;
            case "frequency-8": r.WeeklyFrequency = 8; break;
            case "no-days": r.TravelDays = []; break;
            case "bad-day": r.TravelDays = ["Someday"]; break;
            case "repeated-day": r.TravelDays = ["Monday", "monday"]; break;
            case "bus-type": r.BusType = "Double decker"; break;
            case "purpose": r.TravelPurpose = "Holiday"; break;
        }
        Assert.NotNull(CommuterProfileService.CheckFields(r));
    }

    [Fact]
    public void Days_are_kept_in_week_order_with_their_stored_spelling() =>
        Assert.Equal(["Saturday", "Monday", "Friday"], CommuterProfileService.NormalizeDays(["friday", "MONDAY", " Saturday "]));

    [Theory]
    [InlineData("08:30", 8, 30)]
    [InlineData("7:05", 7, 5)]
    [InlineData("23:59:00", 23, 59)]
    public void Travel_times_are_read_as_24_hour_clock(string text, int hour, int minute) =>
        Assert.Equal(new TimeOnly(hour, minute), CommuterProfileService.ParseTime(text));

    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1")
        .UseSnakeCaseNamingConvention().Options);

    public static TheoryData<string> Queries => new() { "mine", "count", "find", "delete" };

    // Every query EF has to translate is valid SQL; it can only fail because there is no database.
    [Theory]
    [MemberData(nameof(Queries))]
    public async Task Query_is_valid_and_only_fails_on_the_missing_database(string name)
    {
        await using var db = Db();
        var service = new CommuterProfileService(db, null!);
        var error = await Record.ExceptionAsync(() => name switch
        {
            "mine" => service.MineAsync(Guid.NewGuid()),
            "count" => service.CountAsync(Guid.NewGuid()),
            "find" => service.FindAsync(Guid.NewGuid(), Guid.NewGuid()),
            "delete" => service.DeleteAsync(Guid.NewGuid(), Guid.NewGuid()),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        });
        Assert.NotNull(error);
        Assert.True(error is not InvalidOperationException || error.Message.Contains("transient failure"), $"{name}: {error.Message} | {error.InnerException?.Message}");
    }
}

// F19 end to end against PostgreSQL (skipped without SMARTCOMMUTE_TEST_DB).
[Collection(ServerCollection.Name)]
public class TravelPreferenceFlowTests(TestServerFixture server)
{
    private const string Api = "/api/commuter-profile";

    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<Guid> CityBusIdAsync()
    {
        await using var scope = server.Scope();
        var routes = scope.ServiceProvider.GetRequiredService<BusRouteCache>();
        return Guid.Parse((await routes.AllAsync()).First(b => b.IsDhakaCity).Id);
    }

    private static object Body(Guid busId, string name, string from = "Mirpur 10", string to = "Shahbag", string purpose = "University", int frequency = 5,
        string[]? days = null, string busType = "Non-AC", string time = "08:30") => new
    {
        preferenceName = name, startLocation = from, destination = to, busId, preferredTravelTime = time, weeklyFrequency = frequency,
        travelDays = days ?? ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday"], busType, travelPurpose = purpose,
        userId = Guid.NewGuid(), // ignored: the owner always comes from the token
    };

    private static async Task<string> CreateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync(Api, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonOf(response)).GetProperty("data").GetProperty("id").GetString()!;
    }

    [SkippableFact]
    public async Task A_user_keeps_many_independent_preferences()
    {
        RequireServer();
        var bus = await CityBusIdAsync();
        var (owner, client) = await server.CreateUserAsync();

        var university = await CreateAsync(client, Body(bus, "University Journey"));
        var office = await CreateAsync(client, Body(bus, "Office Journey", to: "Motijheel", purpose: "Office", frequency: 6, busType: "AC"));
        await CreateAsync(client, Body(bus, "Coaching", to: "Farmgate", purpose: "Coaching", frequency: 3, days: ["Saturday", "Monday", "Wednesday"]));
        await CreateAsync(client, Body(bus, "Gym", from: "Motijheel", to: "Gulshan 1", purpose: "Personal", frequency: 2, days: ["Sunday", "Tuesday"], time: "18:15"));
        await CreateAsync(client, Body(bus, "Shopping Mall", to: "Bashundhara City", purpose: "Shopping", frequency: 1, days: ["Friday"], busType: "Both"));

        var my = await JsonOf(await client.GetAsync($"{Api}/my"));
        Assert.Equal(5, my.GetProperty("count").GetInt32());
        Assert.Equal(5, my.GetProperty("data").GetArrayLength());
        Assert.Equal(5, (await JsonOf(await client.GetAsync($"{Api}/my/count"))).GetProperty("count").GetInt32());

        var one = (await JsonOf(await client.GetAsync($"{Api}/{office}"))).GetProperty("data");
        Assert.Equal("Office Journey", one.GetProperty("preferenceName").GetString());
        Assert.Equal("AC", one.GetProperty("busType").GetString());
        Assert.False(string.IsNullOrEmpty(one.GetProperty("frequentlyUsedBus").GetString())); // copied from the bus list

        // Editing one leaves the others as they were.
        var updated = await client.PutAsJsonAsync($"{Api}/{university}", Body(bus, "University (morning)", time: "07:45", frequency: 4,
            days: ["Sunday", "Monday", "Tuesday", "Wednesday"]));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var after = (await JsonOf(updated)).GetProperty("data");
        Assert.Equal("07:45", after.GetProperty("preferredTravelTime").GetString());
        Assert.Equal(4, after.GetProperty("travelDays").GetArrayLength());
        var officeAgain = (await JsonOf(await client.GetAsync($"{Api}/{office}"))).GetProperty("data");
        Assert.Equal("Office Journey", officeAgain.GetProperty("preferenceName").GetString());
        Assert.Equal(6, officeAgain.GetProperty("weeklyFrequency").GetInt32());

        // Deleting one leaves the rest.
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"{Api}/{office}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Api}/{office}")).StatusCode);
        Assert.Equal(4, (await JsonOf(await client.GetAsync($"{Api}/my/count"))).GetProperty("count").GetInt32());

        // Stored in the database for this user (survives a restart, unlike anything kept in memory).
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(4, await db.TravelPreferences.CountAsync(p => p.UserId == owner.Id));
    }

    [SkippableFact]
    public async Task Nobody_else_can_see_change_or_delete_a_preference()
    {
        RequireServer();
        var bus = await CityBusIdAsync();
        var (_, owner) = await server.CreateUserAsync();
        var (_, other) = await server.CreateUserAsync();
        var id = await CreateAsync(owner, Body(bus, "Home to Office", to: "Motijheel", purpose: "Office"));

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{Api}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"{Api}/{id}", Body(bus, "Taken over"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"{Api}/{id}")).StatusCode);
        Assert.Equal(0, (await JsonOf(await other.GetAsync($"{Api}/my"))).GetProperty("count").GetInt32());
        var still = (await JsonOf(await owner.GetAsync($"{Api}/{id}"))).GetProperty("data");
        Assert.Equal("Home to Office", still.GetProperty("preferenceName").GetString());

        var anonymous = server.ClientFor(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"{Api}/my")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(Api, Body(bus, "Anonymous"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"{Api}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"{Api}/{id}")).StatusCode);
    }

    [SkippableFact]
    public async Task Bad_preferences_are_refused_before_saving()
    {
        RequireServer();
        var bus = await CityBusIdAsync();
        var (user, client) = await server.CreateUserAsync();

        async Task Refused(object body, HttpStatusCode expected = HttpStatusCode.BadRequest) =>
            Assert.Equal(expected, (await client.PostAsJsonAsync(Api, body)).StatusCode);

        await Refused(new { startLocation = "Mirpur", destination = "Shahbag" }); // required fields missing
        await Refused(Body(bus, "Weekly", frequency: 0));
        await Refused(Body(bus, "Weekly", frequency: 8));
        await Refused(Body(bus, "Days", days: []));
        await Refused(Body(bus, "Days", days: ["Funday"]));
        await Refused(Body(bus, "Type", busType: "Double decker"));
        await Refused(Body(bus, "Purpose", purpose: "Holiday"));
        await Refused(Body(bus, "Time", time: "8 o'clock"));
        await Refused(Body(Guid.NewGuid(), "Unknown bus"));

        await CreateAsync(client, Body(bus, "Same name"));
        await Refused(Body(bus, "same NAME", to: "Motijheel"), HttpStatusCode.Conflict);

        await using var scope = server.Scope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().TravelPreferences.CountAsync(p => p.UserId == user.Id));
    }
}
