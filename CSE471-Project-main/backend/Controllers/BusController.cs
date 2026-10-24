using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Extensions;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

[Authorize]
[Route("bus")]
public class BusController(AppDbContext db) : ApiControllerBase
{
    [HttpGet("all")]
    public async Task<IActionResult> All()
    {
        var buses = await db.Buses.Include(b => b.Stops).OrderBy(b => b.BusName).AsNoTracking().ToListAsync();
        return Ok(new { success = true, count = buses.Count, data = buses.Select(BusViewModel.From) });
    }

    [HttpGet("search-by-name")]
    public async Task<IActionResult> SearchByName([FromQuery] string? busName)
    {
        if (string.IsNullOrWhiteSpace(busName))
        {
            return BadRequestError("busName is required.");
        }

        var pattern = busName.ContainsPattern();
        var buses = await db.Buses
            .Include(b => b.Stops)
            .Where(b => EF.Functions.ILike(b.BusName, pattern) ||
                        (b.RouteNumber != null && EF.Functions.ILike(b.RouteNumber, pattern)) ||
                        (b.Operator != null && EF.Functions.ILike(b.Operator, pattern)))
            .OrderBy(b => b.BusName)
            .AsNoTracking()
            .ToListAsync();
        return Ok(new { success = true, count = buses.Count, data = buses.Select(BusViewModel.From) });
    }

    // Buses that stop at both places. Routes run in both directions, so stop order does not matter.
    [HttpGet("search-by-route")]
    public async Task<IActionResult> SearchByRoute([FromQuery] string? startLocation, [FromQuery] string? endLocation)
    {
        if (string.IsNullOrWhiteSpace(startLocation) || string.IsNullOrWhiteSpace(endLocation))
        {
            return BadRequestError("startLocation and endLocation are required.");
        }

        var startPattern = startLocation.ContainsPattern();
        var endPattern = endLocation.ContainsPattern();
        var candidates = await db.Buses
            .Include(b => b.Stops)
            .Where(b => b.Stops.Any(s => EF.Functions.ILike(s.Name, startPattern)) &&
                        b.Stops.Any(s => EF.Functions.ILike(s.Name, endPattern)))
            .OrderBy(b => b.BusName)
            .AsNoTracking()
            .ToListAsync();

        var start = startLocation.Trim();
        var end = endLocation.Trim();
        var buses = candidates.Where(b =>
        {
            var from = b.Stops.FirstOrDefault(s => s.Name.Contains(start, StringComparison.OrdinalIgnoreCase));
            var to = b.Stops.FirstOrDefault(s => s.Name.Contains(end, StringComparison.OrdinalIgnoreCase) && s.Id != from?.Id);
            return from is not null && to is not null;
        }).ToList();

        return Ok(new { success = true, count = buses.Count, data = buses.Select(BusViewModel.From) });
    }

    // Physical vehicles running on a route.
    [HttpGet("individual/{busInfoId:guid}")]
    public async Task<IActionResult> Individual(Guid busInfoId)
    {
        var buses = await db.IndividualBuses
            .Where(b => b.BusId == busInfoId)
            .OrderBy(b => b.BusCode)
            .AsNoTracking()
            .ToListAsync();
        if (buses.Count == 0)
        {
            return NotFoundError("No individual buses found for this route.");
        }
        return Ok(new { success = true, data = buses.Select(IndividualBusViewModel.From) });
    }
}

[Authorize]
[Route("individual-bus")]
public class IndividualBusController(AppDbContext db) : ApiControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var bus = await db.IndividualBuses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
        return bus is null
            ? NotFoundError("Bus not found.")
            : Ok(new { success = true, data = IndividualBusViewModel.From(bus) });
    }

    [HttpPost("board")]
    public async Task<IActionResult> Board(BusIdRequest request)
    {
        var bus = await db.IndividualBuses.FindAsync(request.BusId);
        if (bus is null)
        {
            return NotFoundError("Bus not found.");
        }

        var userId = CurrentUserId;
        var active = await db.BusBoardings.Include(x => x.IndividualBus)
            .FirstOrDefaultAsync(x => x.UserId == userId && x.EndedAt == null);
        if (active?.IndividualBusId == bus.Id)
        {
            return Ok(new { success = true, message = "You are already on this bus.", data = IndividualBusViewModel.From(bus) });
        }
        if (bus.CurrentPassengerCount >= bus.TotalPassengerCapacity)
        {
            return BadRequestError("This bus is full.");
        }

        // A rider can only be on one bus; leaving the previous one frees that seat.
        if (active is not null)
        {
            active.EndedAt = DateTime.UtcNow;
            active.IndividualBus!.CurrentPassengerCount = Math.Max(0, active.IndividualBus.CurrentPassengerCount - 1);
        }

        bus.CurrentPassengerCount++;
        db.BusBoardings.Add(new BusBoarding { UserId = userId, IndividualBusId = bus.Id });
        await db.SaveChangesAsync();
        return Ok(new { success = true, message = "Boarded successfully.", data = IndividualBusViewModel.From(bus) });
    }

    [HttpPost("end-trip")]
    public async Task<IActionResult> EndTrip(BusIdRequest request)
    {
        var userId = CurrentUserId;
        var boarding = await db.BusBoardings.Include(x => x.IndividualBus)
            .FirstOrDefaultAsync(x => x.UserId == userId && x.EndedAt == null && x.IndividualBusId == request.BusId);
        if (boarding is null)
        {
            return BadRequestError("You are not currently on this bus.");
        }

        boarding.EndedAt = DateTime.UtcNow;
        var bus = boarding.IndividualBus!;
        bus.CurrentPassengerCount = Math.Max(0, bus.CurrentPassengerCount - 1);
        await db.SaveChangesAsync();
        return Ok(new { success = true, message = "Trip ended.", data = IndividualBusViewModel.From(bus) });
    }
}
