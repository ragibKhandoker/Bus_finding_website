using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

// Every route with its stops, kept in memory. The database is far away (Neon), so loading ~200 routes and their
// stops takes seconds; routes only change when the seeder runs, so one load serves every visitor. The copy is
// reloaded after a while so a route edited directly in the database still shows up without a restart.
public class BusRouteCache(IServiceScopeFactory scopes)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<BusViewModel>? _routes;
    private Dictionary<string, BusViewModel> _byRouteNumber = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _loadedAt;

    public async Task<List<BusViewModel>> AllAsync(CancellationToken cancellationToken = default)
    {
        var routes = _routes;
        if (routes is not null && DateTime.UtcNow - _loadedAt < MaxAge)
        {
            return routes;
        }

        // One load at a time, so a burst of first visitors does not each query the database.
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_routes is not null && DateTime.UtcNow - _loadedAt < MaxAge)
            {
                return _routes;
            }
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var buses = await db.Buses.Include(b => b.Stops).OrderBy(b => b.BusName).AsNoTracking().ToListAsync(cancellationToken);
            routes = buses.Select(BusViewModel.From).ToList();
            _byRouteNumber = routes.Where(r => r.RouteNumber is not null)
                .GroupBy(r => r.RouteNumber!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            _routes = routes;
            _loadedAt = DateTime.UtcNow;
            return routes;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<BusViewModel?> ByRouteNumberAsync(string routeNumber, CancellationToken cancellationToken = default)
    {
        await AllAsync(cancellationToken);
        return _byRouteNumber.GetValueOrDefault(routeNumber.Trim());
    }
}
