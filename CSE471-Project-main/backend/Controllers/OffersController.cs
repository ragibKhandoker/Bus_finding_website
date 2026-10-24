using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

[Authorize]
[Route("offers")]
public class OffersController(AppDbContext db, WalletService wallets) : ApiControllerBase
{
    // Returns the offers object itself (not wrapped) because the clients parse the body directly.
    [HttpGet("user")]
    public async Task<IActionResult> Get()
    {
        var offer = await wallets.GetOrCreateOffersAsync(CurrentUserId);
        await db.SaveChangesAsync();
        return Ok(OfferViewModel.From(offer));
    }

    // Granting offers creates spendable value, so only admins may do it.
    [Authorize(Roles = UserRoles.Admin)]
    [HttpPut("user")]
    public async Task<IActionResult> Update(UpdateOffersRequest request)
    {
        var offer = await wallets.GetOrCreateOffersAsync(request.UserId ?? CurrentUserId);
        offer.Cashback = request.Cashback;
        offer.Coupon = request.Coupon;
        offer.Discount = request.Discount;
        offer.IsActive = request.IsActive;
        offer.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(OfferViewModel.From(offer));
    }

    [Authorize(Roles = UserRoles.Admin)]
    [HttpPost("add-cashback")]
    public Task<IActionResult> AddCashback(AdminAmountRequest request) => Add(request, o => o.Cashback += request.Amount);

    [Authorize(Roles = UserRoles.Admin)]
    [HttpPost("add-coupon")]
    public Task<IActionResult> AddCoupon(AdminAmountRequest request) => Add(request, o => o.Coupon += request.Amount);

    [Authorize(Roles = UserRoles.Admin)]
    [HttpPost("add-discount")]
    public Task<IActionResult> AddDiscount(AdminAmountRequest request) => Add(request, o => o.Discount += request.Amount);

    [HttpPost("use-cashback")]
    public Task<IActionResult> UseCashback(AmountRequest request) =>
        Use(request.Amount, "cashback", o => o.Cashback, (o, v) => o.Cashback = v, creditsWallet: true);

    [HttpPost("use-coupon")]
    public Task<IActionResult> UseCoupon(AmountRequest request) =>
        Use(request.Amount, "coupon", o => o.Coupon, (o, v) => o.Coupon = v, creditsWallet: true);

    // A discount is spent against a fare the app has already reduced, so it is not paid into the wallet.
    [HttpPost("use-discount")]
    public Task<IActionResult> UseDiscount(AmountRequest request) =>
        Use(request.Amount, "discount", o => o.Discount, (o, v) => o.Discount = v, creditsWallet: false);

    private async Task<IActionResult> Add(AdminAmountRequest request, Action<Offer> apply)
    {
        var offer = await wallets.GetOrCreateOffersAsync(request.UserId ?? CurrentUserId);
        apply(offer);
        offer.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(OfferViewModel.From(offer));
    }

    private async Task<IActionResult> Use(decimal amount, string kind, Func<Offer, decimal> get, Action<Offer, decimal> set, bool creditsWallet)
    {
        var offer = await wallets.GetOrCreateOffersAsync(CurrentUserId);
        if (!offer.IsActive)
        {
            return BadRequestError("Your offers are not active.");
        }
        if (get(offer) < amount)
        {
            return BadRequestError($"Not enough {kind} available.");
        }

        set(offer, get(offer) - amount);
        offer.UpdatedAt = DateTime.UtcNow;
        if (creditsWallet)
        {
            wallets.Credit(offer.Wallet!, amount, kind, $"Converted {kind} to balance");
        }
        await db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = creditsWallet ? $"৳{amount} {kind} added to your wallet." : $"৳{amount} discount applied.",
            offers = OfferViewModel.From(offer),
            wallet = WalletViewModel.From(offer.Wallet!),
        });
    }
}
