using System.Text.RegularExpressions;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

// A place the rider typed, resolved against the stop names in the database.
// Names: every stop name the place refers to (normalised). Stop: one representative point for maps and distance.
public record ResolvedPlace(HashSet<string> Names, BusStopViewModel Stop);

// Route search rules: a bus matches when it stops at both places with the start before the destination in its
// stored stop order, or in reverse order when the bus also runs the return trip (Bus.RunsBothWays).
public static class BusRouteMatcher
{
    // Lower case with runs of whitespace collapsed, so "  mirpur   10 " matches "Mirpur 10".
    public static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    // Which stops a typed place means: every stop name containing it as whole words, so "Uttara" finds
    // "Jashimuddin (Uttara)" and "Uttara House Building" but "Mirpur 1" never finds "Mirpur 10". Text that is not
    // a whole word anywhere (a name still being typed, like "Farmg") falls back to a plain substring match.
    // Null when no stop matches.
    public static ResolvedPlace? Resolve(IEnumerable<BusViewModel> routes, string place)
    {
        var term = Normalize(place);
        if (term.Length == 0)
        {
            return null;
        }

        var stops = routes.SelectMany(r => r.Stops).ToList();
        var word = new Regex($@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(term)}(?![\p{{L}}\p{{N}}])", RegexOptions.CultureInvariant);
        var matches = stops.Where(s => word.IsMatch(Normalize(s.Name))).ToList();
        if (matches.Count == 0)
        {
            matches = stops.Where(s => Normalize(s.Name).Contains(term)).ToList();
        }
        if (matches.Count == 0)
        {
            return null;
        }

        // The point shown on the map: the stop named exactly as typed, otherwise the matching stop on the most
        // routes. The same stop has slightly different coordinates on different routes, so use their average.
        var names = matches.Select(s => Normalize(s.Name)).ToHashSet();
        var best = matches.GroupBy(s => Normalize(s.Name))
            .OrderByDescending(g => g.Key == term)
            .ThenByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .First();
        var stop = new BusStopViewModel { Name = best.First().Name, Lat = best.Average(s => s.Lat), Lng = best.Average(s => s.Lng) };
        return new ResolvedPlace(names, stop);
    }

    // How the bus serves the trip, or null when it does not. Picks the shortest ride when a place matches
    // several stops on the route.
    public static RouteMatchViewModel? Match(BusViewModel bus, ResolvedPlace start, ResolvedPlace end)
    {
        var stops = bus.Stops;
        var from = Positions(stops, start.Names);
        var to = Positions(stops, end.Names);

        if (BestPair(from, to, (i, j) => j > i) is { } forward)
        {
            return Build("forward", stops.Skip(forward.From).Take(forward.To - forward.From + 1));
        }
        if (!bus.RunsBothWays)
        {
            return null;
        }

        // The return trip: the same stops run backwards, so the start comes after the destination in stored order.
        return BestPair(from, to, (i, j) => j < i) is { } back
            ? Build("return", stops.Skip(back.To).Take(back.From - back.To + 1).Reverse())
            : null;
    }

    private static List<int> Positions(List<BusStopViewModel> stops, HashSet<string> names) =>
        stops.Select((stop, index) => (stop, index)).Where(x => names.Contains(Normalize(x.stop.Name))).Select(x => x.index).ToList();

    // The closest allowed (from, to) pair of stop positions, or null when there is none.
    private static (int From, int To)? BestPair(List<int> from, List<int> to, Func<int, int, bool> allowed)
    {
        var pairs = from.SelectMany(i => to.Where(j => allowed(i, j)).Select(j => (From: i, To: j))).ToList();
        return pairs.Count == 0 ? null : pairs.MinBy(p => Math.Abs(p.To - p.From));
    }

    private static RouteMatchViewModel Build(string direction, IEnumerable<BusStopViewModel> ridden)
    {
        var segment = ridden.ToList();
        return new RouteMatchViewModel { Direction = direction, Boarding = segment[0], Alighting = segment[^1], Segment = segment };
    }
}
