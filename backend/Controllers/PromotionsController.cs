using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// F15: Refer & Earn. The signed-in user's own code and referrals only.
[Authorize]
[Route("referrals")]
public class ReferralsController(ReferralService referrals) : ApiControllerBase
{
    [HttpGet("my-code")]
    public async Task<IActionResult> MyCode() => Ok(new { success = true, code = await referrals.CodeAsync(CurrentUserId) });

    [HttpGet("summary")]
    public async Task<IActionResult> Summary() => Ok(new { success = true, data = await referrals.SummaryAsync(CurrentUserId) });

    [HttpGet("history")]
    public async Task<IActionResult> History() => Ok(new { success = true, data = await referrals.HistoryAsync(CurrentUserId) });

    // A friend's code, entered after signing up: { "code": "DHK7K2M9Q" }.
    [EnableRateLimiting("strict")]
    [HttpPost("apply")]
    public async Task<IActionResult> Apply(ApplyReferralRequest request)
    {
        var result = await referrals.ApplyAsync(CurrentUserId, request.Code);
        return result.Succeeded
            ? StatusCode(result.StatusCode, new { success = true, message = result.Message, status = result.Referral!.Status })
            : Error(result.StatusCode, result.Message);
    }
}

// F15: loyalty tiers.
[Route("membership")]
public class MembershipController(MembershipService membership, RewardConfigService config) : ApiControllerBase
{
    [Authorize]
    [HttpGet("my")]
    public async Task<IActionResult> My() => Ok(new { success = true, data = MembershipViewModel.From(await membership.MyAsync(CurrentUserId)) });

    // The tiers and what they give are public.
    [AllowAnonymous]
    [HttpGet("tiers")]
    public async Task<IActionResult> Tiers() => Ok(new { success = true, data = (await config.TiersAsync()).Select(TierViewModel.From) });
}

// F15: live promotional campaigns. Anyone may see them; signed-in users also see whether they qualify.
[Route("campaigns")]
public class CampaignsController(CampaignService campaigns, RewardConfigService config) : ApiControllerBase
{
    [AllowAnonymous]
    [HttpGet("active")]
    public async Task<IActionResult> Active()
    {
        var now = DateTime.UtcNow;
        var list = await campaigns.ActiveAsync(IsSignedIn ? CurrentUserId : null, now);
        var tiers = await config.TiersAsync();
        return Ok(new { success = true, count = list.Count, data = list.Select(v => CampaignViewModel.From(v.Campaign, tiers, now, v)) });
    }
}

// F15: the admin's Offers & rewards section. Admins only; every change is written to the reward audit log.
[Authorize(Roles = UserRoles.Admin)]
[Route("admin")]
public class AdminPromotionsController(PromotionAdminService admin, RewardConfigService config) : ApiControllerBase
{
    // ---- Coupons ----

    [HttpGet("offers")]
    public async Task<IActionResult> Coupons()
    {
        var tiers = await config.TiersAsync();
        var list = await admin.CouponsAsync();
        return Ok(new { success = true, count = list.Count, data = list.Select(c => CouponViewModel.From(c, tiers)) });
    }

    [HttpPost("offers")]
    public async Task<IActionResult> CreateCoupon(CouponRequest request) => await Answer(await admin.CreateCouponAsync(CurrentUserId, request));

    [HttpPut("offers/{id:guid}")]
    public async Task<IActionResult> UpdateCoupon(Guid id, CouponRequest request) => await Answer(await admin.UpdateCouponAsync(CurrentUserId, id, request));

    [HttpDelete("offers/{id:guid}")]
    public async Task<IActionResult> DeleteCoupon(Guid id)
    {
        var result = await admin.DeleteCouponAsync(CurrentUserId, id);
        return result.Succeeded ? Ok(new { success = true, message = result.Message }) : Error(result.StatusCode, result.Message);
    }

    // Hands a coupon to one user: { "email": "…" }.
    [HttpPost("offers/{id:guid}/issue")]
    public async Task<IActionResult> IssueCoupon(Guid id, IssueCouponRequest request)
    {
        var result = await admin.IssueCouponAsync(CurrentUserId, id, request.Email);
        return result.Succeeded ? StatusCode(result.StatusCode, new { success = true, message = result.Message }) : Error(result.StatusCode, result.Message);
    }

    [HttpGet("offers/statistics")]
    public async Task<IActionResult> Statistics() => Ok(new { success = true, data = await admin.StatisticsAsync(DateTime.UtcNow) });

    [HttpGet("offers/redemptions")]
    public async Task<IActionResult> Redemptions([FromQuery] string? code, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (items, total) = await admin.RedemptionsAsync(code, page, pageSize);
        return Ok(new { success = true, page, pageSize, total, data = items.Select(r => RedemptionViewModel.From(r, withUser: true)) });
    }

    [HttpGet("offers/suspicious")]
    public async Task<IActionResult> Suspicious() => Ok(new { success = true, data = await admin.SuspiciousAsync(DateTime.UtcNow) });

    [HttpGet("offers/audit")]
    public async Task<IActionResult> AuditLog() => Ok(new
    {
        success = true,
        data = (await admin.AuditAsync()).Select(a => new { id = a.Id, a.ActorId, a.Action, a.EntityType, a.EntityId, a.Details, a.CreatedAt }),
    });

    // ---- Campaigns ----

    [HttpGet("campaigns")]
    public async Task<IActionResult> Campaigns()
    {
        var tiers = await config.TiersAsync();
        var now = DateTime.UtcNow;
        return Ok(new { success = true, data = (await admin.CampaignsAsync()).Select(c => CampaignViewModel.From(c, tiers, now)) });
    }

    [HttpPost("campaigns")]
    public async Task<IActionResult> CreateCampaign(CampaignRequest request) => await Answer(await admin.CreateCampaignAsync(CurrentUserId, request));

    [HttpPut("campaigns/{id:guid}")]
    public async Task<IActionResult> UpdateCampaign(Guid id, CampaignRequest request) => await Answer(await admin.UpdateCampaignAsync(CurrentUserId, id, request));

    // { "status": "active" | "paused" | "expired" }
    [HttpPost("campaigns/{id:guid}/status")]
    public async Task<IActionResult> CampaignStatus(Guid id, CampaignStatusRequest request) => await Answer(await admin.SetCampaignStatusAsync(CurrentUserId, id, request.Status));

    // ---- Settings, tiers and referrals ----

    [HttpGet("rewards/settings")]
    public async Task<IActionResult> Settings() => Ok(new { success = true, data = RewardSettingsViewModel.From(await config.SettingsAsync()) });

    [HttpPut("rewards/settings")]
    public async Task<IActionResult> SaveSettings(RewardSettingsRequest request)
    {
        var result = await admin.SaveSettingsAsync(CurrentUserId, request);
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, data = RewardSettingsViewModel.From(result.Value!) })
            : Error(result.StatusCode, result.Message);
    }

    [HttpGet("membership/tiers")]
    public async Task<IActionResult> Tiers() => Ok(new { success = true, data = (await config.TiersAsync()).Select(TierViewModel.From) });

    [HttpPut("membership/tiers")]
    public async Task<IActionResult> SaveTiers(TiersRequest request)
    {
        var result = await admin.SaveTiersAsync(CurrentUserId, request.Tiers);
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, data = result.Value!.Select(TierViewModel.From) })
            : Error(result.StatusCode, result.Message);
    }

    [HttpGet("referrals")]
    public async Task<IActionResult> Referrals([FromQuery] string? status) =>
        Ok(new { success = true, data = await admin.ReferralsAsync(string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant()) });

    [HttpPost("referrals/{id:guid}/reject")]
    public async Task<IActionResult> RejectReferral(Guid id, RejectReferralRequest request)
    {
        var result = await admin.RejectReferralAsync(CurrentUserId, id, request.Note);
        return result.Succeeded ? Ok(new { success = true, message = result.Message }) : Error(result.StatusCode, result.Message);
    }

    private async Task<IActionResult> Answer<T>(PromoResult<T> result)
    {
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        var tiers = await config.TiersAsync();
        object? data = result.Value switch
        {
            Coupon c => CouponViewModel.From(c, tiers),
            RewardCampaign c => CampaignViewModel.From(c, tiers, DateTime.UtcNow),
            _ => null,
        };
        return StatusCode(result.StatusCode, new { success = true, message = result.Message, data });
    }
}
