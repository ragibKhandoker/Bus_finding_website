using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// Server-rendered status page at "/" so you can see at a glance that the API and database are up.
public class HomeController(AppDbContext db, ILogger<HomeController> logger) : Controller
{
    [HttpGet("/")]
    public async Task<IActionResult> Index()
    {
        var model = new StatusViewModel { Environment = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().EnvironmentName };
        try
        {
            model.DatabaseConnected = await db.Database.CanConnectAsync();
            if (model.DatabaseConnected)
            {
                model.Users = await db.Users.CountAsync();
                model.Buses = await db.Buses.CountAsync();
                model.Stops = await db.BusStops.Select(s => s.Name).Distinct().CountAsync();
                model.ActiveRides = await db.RidePosts.CountAsync(r => r.Status == Models.RideStatus.Active);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Status page could not reach the database");
            model.DatabaseConnected = false;
        }

        // Scripts and tools that ask for JSON get the same status as data; browsers get the page.
        if (Request.GetTypedHeaders().Accept.Any(a => a.MediaType.Value == "application/json"))
        {
            return Ok(new
            {
                success = true,
                database = model.DatabaseConnected ? "connected" : "unavailable",
                model.DatabaseConnected,
                model.Buses,
                model.Stops,
            });
        }
        return View(model);
    }

    [HttpGet("/health")]
    public async Task<IActionResult> Health()
    {
        var connected = await db.Database.CanConnectAsync();
        return connected
            ? Ok(new { success = true, status = "healthy" })
            : StatusCode(StatusCodes.Status503ServiceUnavailable, new ErrorResponse("Database unavailable"));
    }
}
