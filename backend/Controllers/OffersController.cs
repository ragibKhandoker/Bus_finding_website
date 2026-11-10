using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// Offers: cashback, coupon and discount value. Users see and use their own; only admins add or set value.
// F15 adds coupon codes, the cashback ledger, savings analytics and the summary of the Offers page. Every endpoint
// works on the signed-in user only (the user comes from the token); amounts are always worked out on the server.
[Authorize]
[Route("offers")]
public class OffersController(
    OfferService offers,
    CouponService coupons,
    CashbackService cashback,
    RewardConfigService config,
    RewardInsightsService insights) : ApiControllerBase
{
    // ---- F15: overview ----

    [HttpGet("summary")]
    public async Task<IActionResult> Summary() => Ok(new { success = true, data = await insights.SummaryAsync(CurrentUserId, DateTime.UtcNow) });

    [HttpGet("analytics")]
    public async Task<IActionResult> Analytics() => Ok(new { success = true, data = await insights.AnalyticsAsync(CurrentUserId, DateTime.UtcNow) });

    // ---- F15: coupons ----

    // All of the user's coupons, in three lists.
    [HttpGet("my")]
    public async Task<IActionResult> My()
    {
        var lists = await coupons.ListAsync(CurrentUserId, DateTime.UtcNow);
        var tiers = await config.TiersAsync();
        return Ok(new
        {
            success = true,
            available = lists.Available.Select(i => MyCouponViewModel.From(i, tiers)),
            used = lists.Used.Select(i => MyCouponViewModel.From(i, tiers)),
            expired = lists.Expired.Select(i => MyCouponViewModel.From(i, tiers)),
        });
    }

    [HttpGet("available")]
    public Task<IActionResult> Available() => List(l => l.Available);

    [HttpGet("used")]
    public Task<IActionResult> Used() => List(l => l.Used);

    [HttpGet("expired")]
    public Task<IActionResult> Expired() => List(l => l.Expired);

    private async Task<IActionResult> List(Func<CouponLists, List<CouponListItem>> pick)
    {
        var items = pick(await coupons.ListAsync(CurrentUserId, DateTime.UtcNow));
        var tiers = await config.TiersAsync();
        return Ok(new { success = true, count = items.Count, data = items.Select(i => MyCouponViewModel.From(i, tiers)) });
    }

    // The completed eligible trips a coupon can be used on (boarded and ended in the app, last 7 days).
    [HttpGet("trips")]
    public async Task<IActionResult> EligibleTrips() =>
        Ok(new { success = true, data = (await coupons.EligibleTripsAsync(CurrentUserId, DateTime.UtcNow)).Select(EligibleTripViewModel.From) });

    // Checks a code against a trip and shows what it would save: { "code": "DHAKA10", "tripId": "…" }. A code that
    // exists but does not apply is answered with valid: false and the reason.
    [EnableRateLimiting("coupon")]
    [HttpPost("validate-coupon")]
    public async Task<IActionResult> ValidateCoupon(CouponCodeRequest request)
    {
        var quote = await coupons.QuoteAsync(CurrentUserId, request.Code, request.TripId, DateTime.UtcNow);
        if (!quote.Ok && quote.Coupon is null)
        {
            return Error(quote.StatusCode, quote.Error!);
        }
        var view = CouponQuoteViewModel.From(quote, await config.TiersAsync());
        return Ok(new { success = true, valid = quote.Ok, message = view.Message, data = view });
    }

    // Uses the code on the trip; the saving is paid into the wallet balance. { "code", "tripId", "reference" }.
    [EnableRateLimiting("coupon")]
    [HttpPost("redeem-coupon")]
    public async Task<IActionResult> RedeemCoupon(CouponCodeRequest request)
    {
        var result = await coupons.RedeemAsync(CurrentUserId, request.Code, request.TripId, request.Reference);
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        var wallet = result.Wallet ?? (await offers.EnsureAsync(CurrentUserId)).Wallet;
        return Ok(new
        {
            success = true,
            message = result.Message,
            data = RedemptionViewModel.From(result.Redemption!),
            quote = result.Quote is null ? null : CouponQuoteViewModel.From(result.Quote, await config.TiersAsync()),
            wallet = wallet is null ? null : WalletViewModel.From(wallet),
        });
    }

    // The available coupon that saves the most on this trip, or data: null.
    [HttpGet("best-coupon")]
    public async Task<IActionResult> BestCoupon([FromQuery] Guid tripId)
    {
        var best = await coupons.BestAsync(CurrentUserId, tripId, DateTime.UtcNow);
        return Ok(new { success = true, data = best is null ? null : CouponQuoteViewModel.From(best, await config.TiersAsync()) });
    }

    // ---- F15: cashback ----

    [HttpGet("cashback")]
    public async Task<IActionResult> Cashback() => Ok(new { success = true, data = await cashback.SummaryAsync(CurrentUserId, DateTime.UtcNow) });

    [HttpGet("cashback/history")]
    public async Task<IActionResult> CashbackHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (items, total) = await cashback.HistoryAsync(CurrentUserId, page, pageSize);
        return Ok(new { success = true, page, pageSize, total, data = items.Select(CashbackEntryViewModel.From) });
    }

    // Admin only, and it takes no amount: it (re)runs the reward engine for a completed trip, which works out the
    // cashback from the trip the server recorded. Running it again for the same trip changes nothing.
    [Authorize(Roles = UserRoles.Admin)]
    [HttpPost("cashback/credit")]
    public async Task<IActionResult> CreditCashback(ProcessTripRewardsRequest request, [FromServices] RewardEngine engine, [FromServices] PromotionAdminService admin)
    {
        var outcome = await engine.ProcessTripAsync(request.TripId!.Value);
        admin.AuditTripProcessing(CurrentUserId, request.TripId.Value, outcome);
        await admin.SaveAsync();
        return Ok(new { success = true, message = outcome.Message, data = outcome });
    }

    // ---- Offer balances (before F15) ----

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
