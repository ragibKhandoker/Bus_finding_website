using System.Text.Json;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Data;

// Starter data so a fresh database has buses to search: every Dhaka city bus route listed on dhakabusroutes.com
// (184 routes), read from Data/dhaka-bus-routes.json, which is embedded in the build.
// Stop lists follow the source's order; the site itself marks them as compiled from public listings and not
// field-checked, and the coordinates are approximate landmark positions. Fares are not in the source, so each
// route gets the standard city rate for its bus type.
// The route numbers (DBR-1, DBR-2, ...) are the source's listing ids, not official numbers. The route number is how
// the seeder recognises a route, so do not change it once seeded.
public static class SeedData
{
    private const string RoutesResource = "SmartCommute.Server.Data.dhaka-bus-routes.json";

    private record StopSeed(string Name, double Lat, double Lng);
    private record RouteSeed(string RouteNumber, string Name, string BusType, StopSeed[] Stops);

    private static RouteSeed[] LoadRoutes()
    {
        using var stream = typeof(SeedData).Assembly.GetManifestResourceStream(RoutesResource)
            ?? throw new InvalidOperationException($"Embedded resource {RoutesResource} is missing");
        return JsonSerializer.Deserialize<RouteSeed[]>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
    }

    public static List<Bus> Buses()
    {
        var buses = new List<Bus>();
        foreach (var route in LoadRoutes())
        {
            var seating = route.BusType == "seating";
            var bus = new Bus
            {
                BusName = route.Name,
                RouteNumber = route.RouteNumber,
                BusType = route.BusType,
                BaseFare = seating ? 15 : 10,
                PerKmFare = seating ? 2.60m : 2.45m,
                Stops = route.Stops.Select((stop, index) => new BusStop
                {
                    Order = index,
                    Name = stop.Name,
                    Lat = stop.Lat,
                    Lng = stop.Lng,
                }).ToList(),
            };

            var first = route.Stops[0];
            bus.IndividualBuses = Enumerable.Range(1, 3).Select(n => new IndividualBus
            {
                BusCode = $"{route.RouteNumber}-{n:00}",
                TotalPassengerCapacity = 40,
                Latitude = first.Lat,
                Longitude = first.Lng,
                BusType = route.BusType,
            }).ToList();

            buses.Add(bus);
        }
        return buses;
    }
}
