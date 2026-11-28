using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// QR codes inside buses (see BusQrService). {vehicleId} is an IndividualBus (one physical bus); its route follows
// from it. Managing a code needs an admin or the bus's assigned driver; scanning needs any signed-in rider.
[Authorize]
[Route("bus")]
public class BusQrController(BusQrService qr) : ApiControllerBase
{
    // The vehicle's current code (data is null when it has none yet).
    [HttpGet("{vehicleId:guid}/qr")]
    public async Task<IActionResult> Current(Guid vehicleId)
    {
        var result = await qr.CurrentAsync(CurrentUserId, IsAdmin, vehicleId);
        return result.Succeeded ? Ok(new { success = true, data = result.Value }) : Error(result.StatusCode, result.Message);
    }

    // A new code for the vehicle; the previous one stops working.
    [HttpPost("{vehicleId:guid}/generate-qr")]
    public async Task<IActionResult> Generate(Guid vehicleId)
    {
        var result = await qr.GenerateAsync(CurrentUserId, IsAdmin, vehicleId);
        return result.Succeeded ? Created201(new { success = true, message = result.Message, data = result.Value }) : Error(result.StatusCode, result.Message);
    }

    // Turns the vehicle's code off.
    [HttpDelete("{vehicleId:guid}/qr")]
    public async Task<IActionResult> Deactivate(Guid vehicleId)
    {
        var result = await qr.DeactivateAsync(CurrentUserId, IsAdmin, vehicleId);
        return result.Succeeded ? Ok(new { success = true, message = result.Message }) : Error(result.StatusCode, result.Message);
    }

    // The buses whose codes the caller manages: a driver's assigned buses, or (admin) one route's vehicles.
    [HttpGet("qr/manageable")]
    public async Task<IActionResult> Manageable([FromQuery] Guid? routeId)
    {
        var list = await qr.ManageableAsync(CurrentUserId, IsAdmin, routeId);
        return Ok(new { success = true, count = list.Count, data = list });
    }

    // Checks a scanned code and returns the bus, its route and a one-time boarding ticket.
    [HttpPost("verify-qr")]
    public async Task<IActionResult> Verify(VerifyQrRequest request)
    {
        var result = await qr.VerifyAsync(CurrentUserId, request.Code, request.Lat, request.Lng);
        return result.Succeeded ? Ok(new { success = true, message = result.Message, data = result.Value }) : Error(result.StatusCode, result.Message);
    }
}
