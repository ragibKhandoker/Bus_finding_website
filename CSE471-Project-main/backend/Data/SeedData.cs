using SmartCommute.Server.Models;

namespace SmartCommute.Server.Data;

// Starter data so a fresh database has buses to search. Coordinates are approximate stop locations in Dhaka.
public static class SeedData
{
    private static readonly Dictionary<string, (double Lat, double Lng)> Stops = new()
    {
        ["Tongi"] = (23.8915, 90.4023),
        ["Abdullahpur"] = (23.8790, 90.4010),
        ["House Building"] = (23.8737, 90.4003),
        ["Azampur"] = (23.8676, 90.4006),
        ["Jasimuddin"] = (23.8613, 90.4001),
        ["Airport"] = (23.8513, 90.4086),
        ["Kalshi"] = (23.8217, 90.3858),
        ["Khilkhet"] = (23.8293, 90.4199),
        ["Kuril Bishwa Road"] = (23.8197, 90.4211),
        ["Bashundhara"] = (23.8133, 90.4245),
        ["Natun Bazar"] = (23.7980, 90.4234),
        ["Badda"] = (23.7806, 90.4265),
        ["Rampura"] = (23.7612, 90.4205),
        ["Malibagh"] = (23.7489, 90.4128),
        ["Mouchak"] = (23.7465, 90.4126),
        ["Khilgaon"] = (23.7520, 90.4280),
        ["Kamalapur"] = (23.7321, 90.4262),
        ["Motijheel"] = (23.7330, 90.4172),
        ["Paltan"] = (23.7357, 90.4130),
        ["Gulistan"] = (23.7239, 90.4126),
        ["Sadarghat"] = (23.7067, 90.4103),
        ["Sayedabad"] = (23.7148, 90.4277),
        ["Jatrabari"] = (23.7104, 90.4344),
        ["Banani"] = (23.7937, 90.4066),
        ["Gulshan 1"] = (23.7808, 90.4167),
        ["Gulshan 2"] = (23.7946, 90.4143),
        ["Mohakhali"] = (23.7783, 90.4050),
        ["Bijoy Sarani"] = (23.7650, 90.3900),
        ["Farmgate"] = (23.7577, 90.3897),
        ["Karwan Bazar"] = (23.7508, 90.3930),
        ["Bangla Motor"] = (23.7465, 90.3950),
        ["Shahbag"] = (23.7383, 90.3958),
        ["Press Club"] = (23.7303, 90.4025),
        ["Mirpur 12"] = (23.8270, 90.3640),
        ["Mirpur 10"] = (23.8069, 90.3687),
        ["Mirpur 1"] = (23.7957, 90.3537),
        ["Kazipara"] = (23.7976, 90.3727),
        ["Shewrapara"] = (23.7905, 90.3757),
        ["Agargaon"] = (23.7781, 90.3800),
        ["Gabtoli"] = (23.7840, 90.3446),
        ["Technical"] = (23.7808, 90.3530),
        ["Kalyanpur"] = (23.7820, 90.3610),
        ["Shyamoli"] = (23.7743, 90.3653),
        ["Asad Gate"] = (23.7600, 90.3730),
        ["Mohammadpur"] = (23.7650, 90.3580),
        ["Dhanmondi 27"] = (23.7567, 90.3740),
        ["Science Lab"] = (23.7389, 90.3832),
        ["New Market"] = (23.7330, 90.3844),
        ["Azimpur"] = (23.7280, 90.3860),
    };

    private record RouteSeed(string Name, string RouteNumber, string Operator, string Frequency, string BusType,
        decimal BaseFare, decimal PerKmFare, string[] StopNames);

    private static readonly RouteSeed[] Routes =
    [
        new("Turag Paribahan", "T-1", "Turag Paribahan Ltd.", "Every 5-10 min", "general", 10, 2.45m,
            ["Abdullahpur", "House Building", "Azampur", "Jasimuddin", "Airport", "Khilkhet", "Kuril Bishwa Road",
             "Bashundhara", "Natun Bazar", "Badda", "Rampura", "Malibagh", "Mouchak", "Motijheel"]),
        new("Victor Classic", "VC-3", "Victor Classic Transport", "Every 10 min", "seating", 20, 2.80m,
            ["Abdullahpur", "House Building", "Airport", "Khilkhet", "Kuril Bishwa Road", "Natun Bazar", "Badda",
             "Rampura", "Malibagh", "Kamalapur", "Sadarghat"]),
        new("BRTC Double Decker", "BRTC-6", "Bangladesh Road Transport Corporation", "Every 15 min", "double-decker", 15, 2.45m,
            ["Gabtoli", "Technical", "Kalyanpur", "Shyamoli", "Asad Gate", "Farmgate", "Karwan Bazar", "Shahbag",
             "Press Club", "Paltan", "Motijheel"]),
        new("Akash Paribahan", "A-12", "Akash Paribahan", "Every 8 min", "general", 10, 2.45m,
            ["Mirpur 12", "Mirpur 10", "Kazipara", "Shewrapara", "Agargaon", "Bijoy Sarani", "Farmgate",
             "Karwan Bazar", "Bangla Motor", "Shahbag", "Press Club", "Gulistan", "Jatrabari"]),
        new("Bihanga", "B-8", "Bihanga Paribahan", "Every 10 min", "general", 10, 2.45m,
            ["Mirpur 1", "Mirpur 10", "Kazipara", "Shewrapara", "Agargaon", "Mohakhali", "Gulshan 1", "Badda", "Rampura"]),
        new("Alif", "AL-2", "Alif Enterprise", "Every 7 min", "general", 10, 2.45m,
            ["Mirpur 1", "Technical", "Kalyanpur", "Shyamoli", "Asad Gate", "Dhanmondi 27", "Science Lab",
             "New Market", "Azimpur"]),
        new("Green Dhaka", "GD-1", "Green Dhaka Transport", "Every 20 min", "ac", 30, 4.00m,
            ["House Building", "Airport", "Banani", "Mohakhali", "Farmgate", "Shahbag", "Gulistan", "Jatrabari"]),
        new("Projapoti", "P-5", "Projapoti Paribahan", "Every 10 min", "seating", 15, 2.60m,
            ["Mohammadpur", "Asad Gate", "Farmgate", "Mohakhali", "Banani", "Gulshan 2", "Natun Bazar",
             "Kuril Bishwa Road", "Airport", "Abdullahpur"]),
        new("Balaka", "BL-4", "Balaka Paribahan", "Every 6 min", "general", 10, 2.45m,
            ["Tongi", "Abdullahpur", "House Building", "Airport", "Banani", "Mohakhali", "Farmgate", "Karwan Bazar",
             "Shahbag", "Press Club", "Gulistan", "Sayedabad"]),
        new("Anabil Super", "AN-7", "Anabil Super Service", "Every 10 min", "seating", 15, 2.60m,
            ["Gabtoli", "Mirpur 1", "Mirpur 10", "Kalshi", "Airport", "Khilkhet", "Kuril Bishwa Road", "Natun Bazar",
             "Badda", "Rampura", "Khilgaon", "Kamalapur"]),
        new("Shikhor", "SK-9", "Shikhor Paribahan", "Every 12 min", "general", 10, 2.45m,
            ["Mohammadpur", "Dhanmondi 27", "Science Lab", "Shahbag", "Paltan", "Motijheel", "Kamalapur"]),
        new("Raida", "R-11", "Raida Enterprise", "Every 8 min", "general", 10, 2.45m,
            ["Jatrabari", "Sayedabad", "Kamalapur", "Khilgaon", "Malibagh", "Rampura", "Badda", "Natun Bazar",
             "Kuril Bishwa Road", "Khilkhet", "Airport", "Tongi"]),
    ];

    public static List<Bus> Buses()
    {
        var buses = new List<Bus>();
        foreach (var route in Routes)
        {
            var bus = new Bus
            {
                BusName = route.Name,
                RouteNumber = route.RouteNumber,
                Operator = route.Operator,
                Frequency = route.Frequency,
                BusType = route.BusType,
                BaseFare = route.BaseFare,
                PerKmFare = route.PerKmFare,
                Stops = route.StopNames.Select((name, index) => new BusStop
                {
                    Order = index,
                    Name = name,
                    Lat = Stops[name].Lat,
                    Lng = Stops[name].Lng,
                }).ToList(),
            };

            var first = Stops[route.StopNames[0]];
            bus.IndividualBuses = Enumerable.Range(1, 3).Select(n => new IndividualBus
            {
                BusCode = $"{route.RouteNumber}-{n:00}",
                TotalPassengerCapacity = route.BusType == "double-decker" ? 70 : 40,
                Latitude = first.Lat,
                Longitude = first.Lng,
                BusType = route.BusType,
            }).ToList();

            buses.Add(bus);
        }
        return buses;
    }
}
