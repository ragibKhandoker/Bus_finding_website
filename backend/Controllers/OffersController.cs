using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// Offers: cashback, coupon and discount value. Users see and use their own; only admins add or set value.
[Authorize]
[Route("offers")]
public class OffersController(OfferService offers) : ApiControllerBase
{
    // Returns the offers object itself (not wrapped) because the clients parse the body directly.
    [HttpGet("user")]
    public Task<IActionResult> Get() => Details(CurrentUserId);

    // A user may read their own offers; an admin may read anyone's.
    [HttpGet("user/{userId:guid}")]
    public Task<IActionResult> GetFor(Guid userId) =>
        userId == CurrentUserId || IsAdmin ? Details(userId) : Task.FromResult<IActionResult>(ForbiddenError("You can only see your own offers."));

    private async Task<IActionResult> Details(Guid userId)
    {
        var offer = await offers.EnsureAsync(userId);
        return Ok(OfferDetailsViewModel.From(offer, offers.Rewards, await offers.HistoryAsync(userId), await offers.TripsAsync(userId)));
    }

    // Setting balances creates spendable value, so only admins may do it.
    [Authorize(Roles = UserRoles.Admin)]
    [HttpPut("user")]
    public Task<IActionResult> Update(UpdateOffersRequest request) => Set(request.UserId ?? CurrentUserId, request);

    [Authorize(Roles = UserRoles.Admin)]
    [HttpPut("user/{userId:guid}")]
    public Task<IActionResult> UpdateFor(Guid userId, UpdateOffersRequest request) => Set(userId, request);

    private async Task<IActionResult> Set(Guid userId, UpdateOffersRequest request)
    {
        var result = await offers.SetAsync(CurrentUserId, userId, request.Cashback, request.Coupon, request.Discount, request.IsActive);
        return result.Succeeded ? Ok(OfferViewModel.From(result.Offer!)) : Error(result.StatusCode, result.Message);
    }

    [Authorize(Roles = UserRoles.Admin)]
    [HttpPost("add-cashback")]
    public Task<IActionResult> AddCashback(AdminAmountRequest request) => Grant(request, OfferKinds.Cashback);

    [Authorize(Roles = UserRoles.Admin)]
    [HttpPost("add-coupon")]
    public Task<IActionResult> AddCoupon(AdminAmountRequest request) => Grant(request, OfferKinds.Coupon);

    [Authorize(Roles = UserRoles.Admin)]
    [HttpPost("add-discount")]
    public Task<IActionResult> AddDiscount(AdminAmountRequest request) => Grant(request, OfferKinds.Discount);

    private async Task<IActionResult> Grant(AdminAmountRequest request, string kind)
    {
        var result = await offers.GrantAsync(CurrentUserId, request.UserId ?? CurrentUserId, kind, request.Amount, request.Reference);
        return result.Succeeded ? Ok(OfferViewModel.From(result.Offer!)) : Error(result.StatusCode, result.Message);
    }

    // Pays cashback into the wallet: { "amount": 20, "reference": "…" }, or {} for all of it.
    [HttpPost("use-cashback")]
    public async Task<IActionResult> UseCashback(UseCashbackRequest request) =>
        Used(await offers.RedeemCashbackAsync(CurrentUserId, request.Amount, request.Reference));

    // Takes coupon or discount value off a completed trip: { "tripId": "…", "amount"?: 10, "reference"?: "…" }.
    [HttpPost("use-coupon")]
    public async Task<IActionResult> UseCoupon(UseOnTripRequest request) =>
        Used(await offers.ApplyToTripAsync(CurrentUserId, OfferKinds.Coupon, request.TripId!.Value, request.Amount, request.Reference));

    [HttpPost("use-discount")]
    public async Task<IActionResult> UseDiscount(UseOnTripRequest request) =>
        Used(await offers.ApplyToTripAsync(CurrentUserId, OfferKinds.Discount, request.TripId!.Value, request.Amount, request.Reference));

    private IActionResult Used(OfferResult result)
    {
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        var offer = result.Offer!;
        return Ok(new
        {
            success = true,
            message = result.Message,
            offers = OfferViewModel.From(offer),
            wallet = WalletViewModel.From(offer.Wallet!),
            entry = result.Entry is null ? null : OfferTransactionViewModel.From(result.Entry),
            transaction = result.Entry?.WalletTransaction is { } credit ? WalletTransactionViewModel.From(credit) : null,
            trip = result.Extra,
        });
    }
}
