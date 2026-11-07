using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

// What the status page at "/" and the /health check report about the server and its database.
public class StatusService(AppDbContext db, IWebHostEnvironment environment, ILogger<StatusService> logger)
{
    public async Task<StatusViewModel> StatusAsync()
    {
        var model = new StatusViewModel { Environment = environment.EnvironmentName };
        try
        {
            model.DatabaseConnected = await db.Database.CanConnectAsync();
            if (model.DatabaseConnected)
            {
                model.Users = await db.Users.CountAsync();
                model.Buses = await db.Buses.CountAsync();
                model.Stops = await db.BusStops.Select(s => s.Name).Distinct().CountAsync();
                model.ActiveRides = await db.RidePosts.CountAsync(r => r.Status == RideStatus.Active && r.DeletedAt == null);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Status page could not reach the database");
            model.DatabaseConnected = false;
        }
        return model;
    }

    public Task<bool> IsHealthyAsync() => db.Database.CanConnectAsync();
}
