using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Tests;

// The stops saved on a trip: every stop from boarding to getting off, in the order ridden.
public class TripStopsTests
{
    private static readonly BusViewModel Route = new()
    {
        BusName = "Victor Classic",
        Stops = new[] { "Jatrabari", "Malibagh", "Rampura", "Badda", "Natun Bazar" }
            .Select((name, order) => new BusStopViewModel { Name = name, Order = order }).ToList(),
    };

    [Fact]
    public void Forward_ride_lists_each_stop_in_order()
    {
        Assert.Equal(["Malibagh", "Rampura", "Badda"], TripService.StopsBetween(Route, 1, 3));
    }

    [Fact]
    public void Return_ride_lists_the_stops_backwards()
    {
        Assert.Equal(["Natun Bazar", "Badda", "Rampura"], TripService.StopsBetween(Route, 4, 2));
    }

    [Fact]
    public void Neighbouring_stops_give_both_ends()
    {
        Assert.Equal(["Jatrabari", "Malibagh"], TripService.StopsBetween(Route, 0, 1));
    }
}
