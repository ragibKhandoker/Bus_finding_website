using Microsoft.AspNetCore.Mvc;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// Server-rendered status page at "/" (Views/Home/Index.cshtml) so you can see at a glance that the API and
// database are up. The figures come from StatusService.
public class HomeController(StatusService status) : Controller
{
    [HttpGet("/")]
    public async Task<IActionResult> Index()
    {
        var model = await status.StatusAsync();

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
    public async Task<IActionResult> Health() => await status.IsHealthyAsync()
        ? Ok(new { success = true, status = "healthy" })
        : StatusCode(StatusCodes.Status503ServiceUnavailable, new ErrorResponse("Database unavailable"));
}
