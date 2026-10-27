using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// GPay, the in-app stored-value account (see GpayService). Every call is for the signed-in user; moving money also
// needs the account's PIN. No real payment provider is involved.
[Authorize]
[Route("gpay")]
public class GpayController(GpayService gpay) : ApiControllerBase
{
    // Creates the account with the configured starting balance and returns a 4-digit PIN once.
    [HttpPost("register")]
    public async Task<IActionResult> Register()
    {
        var result = await gpay.RegisterAsync(CurrentUserId);
        return result.Succeeded
            ? Created201(new { success = true, message = result.Message, data = GpayViewModel.From(result.Account!, result.Pin) })
            : Error(result.StatusCode, result.Message);
    }

    // Checks the PIN so the page can unlock; it does not start any session.
    [EnableRateLimiting("strict")]
    [HttpPost("login")]
    public async Task<IActionResult> Login(GpayLoginRequest request)
    {
        var result = await gpay.CheckPinAsync(CurrentUserId, request.Code);
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, data = GpayViewModel.From(result.Account!) })
            : Error(result.StatusCode, result.Message);
    }

    [EnableRateLimiting("strict")]
    [HttpPost("reset-pin")]
    public async Task<IActionResult> ResetPin(GpayResetPinRequest request)
    {
        var result = await gpay.ResetPinAsync(CurrentUserId, request.Password);
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, data = GpayViewModel.From(result.Account!, result.Pin) })
            : Error(result.StatusCode, result.Message);
    }

    [HttpGet("balance")]
    public async Task<IActionResult> Balance()
    {
        var account = await gpay.FindAsync(CurrentUserId);
        return account is null
            ? NotFoundError("GPay account not found.")
            : Ok(new { success = true, data = GpayViewModel.From(account), startingBalance = gpay.StartingBalance });
    }

    // Newest first: ?page=1&pageSize=20 (pageSize at most 100).
    [HttpGet("transactions")]
    public async Task<IActionResult> Transactions([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (items, total) = await gpay.HistoryAsync(CurrentUserId, page, pageSize);
        return Ok(new { success = true, total, page, pageSize, totalPages = (int)Math.Ceiling(total / (double)pageSize), data = items.Select(GpayTransactionViewModel.From) });
    }

    // Recent completed trips with their fare and whether GPay paid them.
    [HttpGet("trips")]
    public async Task<IActionResult> Trips() => Ok(new { success = true, data = (await gpay.TripsAsync(CurrentUserId)).Select(GpayTripViewModel.From) });

    // Moves money from GPay into the commute wallet: { "amount": 100, "pin": "1234", "reference": "…" }.
    [EnableRateLimiting("strict")]
    [HttpPost("recharge")]
    public async Task<IActionResult> Recharge(GpayRechargeRequest request) =>
        Moved(await gpay.RechargeWalletAsync(CurrentUserId, request.Amount, request.Pin, request.Reference));

    // Pays a completed trip's fare: { "tripId": "…", "pin": "1234", "reference": "…" }. The fare comes from the trip.
    [EnableRateLimiting("strict")]
    [HttpPost("pay")]
    public async Task<IActionResult> PayTrip(GpayPayTripRequest request) =>
        Moved(await gpay.PayTripAsync(CurrentUserId, request.TripId!.Value, request.Pin, request.Reference));

    // Older clients: pays an amount from the user's own balance.
    [EnableRateLimiting("strict")]
    [HttpPost("deduct")]
    public async Task<IActionResult> Deduct(GpayDeductRequest request) =>
        Moved(await gpay.PayAmountAsync(CurrentUserId, request.Amount, request.Description, request.Pin, request.Reference));

    // Admin only: test money, since there is no payment provider. { "userId": "…", "amount": 500, "reference": "…" }.
    [Authorize(Roles = UserRoles.Admin)]
    [HttpPost("admin/top-up")]
    public async Task<IActionResult> TopUp(GpayTopUpRequest request)
    {
        var result = await gpay.TopUpAsync(CurrentUserId, request.UserId!.Value, request.Amount, request.Reference);
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, data = GpayViewModel.From(result.Account!) })
            : Error(result.StatusCode, result.Message);
    }

    private IActionResult Moved(GpayResult result) => result.Succeeded
        ? Ok(new
        {
            success = true,
            message = result.Message,
            data = result.Account is null ? null : GpayViewModel.From(result.Account),
            transaction = result.Entry is null ? null : GpayTransactionViewModel.From(result.Entry),
            wallet = result.Wallet is null ? null : WalletViewModel.From(result.Wallet),
        })
        : Error(result.StatusCode, result.Message);
}
