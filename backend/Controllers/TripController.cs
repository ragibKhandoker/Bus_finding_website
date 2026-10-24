using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

[Authorize]
[Route("trip")]
public class TripController(AppDbContext db, WalletService wallets) : ApiControllerBase
{
    // Logs a trip in history only.
    [HttpPost("add")]
    public async Task<IActionResult> Add(AddTripRequest request)
    {
        var trip = NewTrip(request);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();
        return Created201(new { success = true, message = "Trip added.", data = TripViewModel.From(trip) });
    }

    // Completes a bus trip: logs it and awards gems.
    [HttpPost("")]
    public async Task<IActionResult> Complete(AddTripRequest request)
    {
        var trip = NewTrip(request);
        db.Trips.Add(trip);
        var wallet = await wallets.GetOrCreateAsync(CurrentUserId);
        wallets.AddGems(wallet, wallets.Rewards.GemsPerTrip, $"Trip on {trip.BusName}");
        await db.SaveChangesAsync();

        return Created201(new
        {
            success = true,
            message = $"Trip completed. You earned {wallets.Rewards.GemsPerTrip} gems.",
            data = TripViewModel.From(trip),
            gemsEarned = wallets.Rewards.GemsPerTrip,
            wallet = WalletViewModel.From(wallet),
        });
    }

    [HttpGet("user")]
    public Task<IActionResult> UserTrips() => ListTrips();

    [HttpGet("")]
    public Task<IActionResult> List() => ListTrips();

    private async Task<IActionResult> ListTrips()
    {
        var trips = await db.Trips
            .Where(t => t.UserId == CurrentUserId)
            .OrderByDescending(t => t.CreatedAt)
            .AsNoTracking()
            .ToListAsync();
        return Ok(new { success = true, count = trips.Count, data = trips.Select(TripViewModel.From) });
    }

    private Trip NewTrip(AddTripRequest request) => new()
    {
        UserId = CurrentUserId,
        BusId = request.BusId.Trim(),
        BusName = string.IsNullOrWhiteSpace(request.BusName) ? "Bus" : request.BusName.Trim(),
        Distance = request.Distance,
        Fare = request.Fare,
        Source = request.Source?.Trim() ?? "",
        Destination = request.Destination?.Trim() ?? "",
    };
}
