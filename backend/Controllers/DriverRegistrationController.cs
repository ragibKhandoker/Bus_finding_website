using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartCommute.Server.Extensions;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// F21: driver registration, for the driver. Registering is public (and rate limited); everything else needs a
// driver's sign-in and only ever acts on the signed-in driver's own application. Nothing here can change a
// verification status except a resubmission, which only moves RESUBMISSION_REQUIRED back to PENDING.
[Route("api/driver-registration")]
public class DriverRegistrationController(DriverRegistrationService drivers) : ApiControllerBase
{
    // Fields the form needs: licence and employment types, districts, document types and size limit.
    [HttpGet("options")]
    public IActionResult Options([FromServices] Microsoft.Extensions.Options.IOptions<DriverVerificationOptions> options) => Ok(new
    {
        success = true,
        licenseTypes = DriverLicenseTypes.All,
        employmentTypes = DriverEmploymentTypes.All,
        districts = DriverRules.Districts,
        genders = UserGenders.All,
        documentTypes = DriverDocumentTypes.All,
        requiredDocuments = DriverDocumentTypes.Required,
        maxImageBytes = options.Value.MaxImageBytes,
        minimumAge = options.Value.MinimumAge,
    });

    // The whole application in one request: details plus documents as data URLs. Creates a driver account with
    // PENDING verification and signs it in.
    [AllowAnonymous]
    [EnableRateLimiting("driver-register")]
    [HttpPost("register")]
    [RequestSizeLimit(40 * 1024 * 1024)]
    public async Task<IActionResult> Register(DriverRegistrationRequest request)
    {
        if (IsSignedIn)
        {
            return BadRequestError("Sign out before registering a new driver account.");
        }
        var result = await drivers.RegisterAsync(request, new ClientInfo(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString()));
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        var (signedIn, profile) = result.Value!;
        return Created201(new
        {
            success = true,
            message = result.Message,
            token = signedIn.Token,
            expiresAt = signedIn.ExpiresAt,
            user = UserViewModel.From(signedIn.User),
            data = DriverStatusViewModel.From(profile, []),
        });
    }

    [Authorize(Roles = UserRoles.Driver)]
    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var profile = await drivers.ProfileOfAsync(CurrentUserId);
        return Ok(new
        {
            success = true,
            data = profile is null ? DriverStatusViewModel.NotRegistered : DriverStatusViewModel.From(profile, await drivers.AssignmentsOfAsync(CurrentUserId)),
        });
    }

    [Authorize(Roles = UserRoles.Driver)]
    [HttpGet("my-profile")]
    public async Task<IActionResult> MyProfile()
    {
        var profile = await drivers.ProfileOfAsync(CurrentUserId);
        return profile is null
            ? NotFoundError("No driver registration was found for this account.")
            : Ok(new { success = true, data = await View(profile) });
    }

    [Authorize(Roles = UserRoles.Driver)]
    [HttpPut("update")]
    public async Task<IActionResult> Update(DriverProfileUpdateRequest request)
    {
        var result = await drivers.UpdateContactAsync(CurrentUserId, request);
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, data = await View(result.Value!) })
            : Error(result.StatusCode, result.Message);
    }

    [Authorize(Roles = UserRoles.Driver)]
    [EnableRateLimiting("strict")]
    [HttpPost("resubmit")]
    [RequestSizeLimit(40 * 1024 * 1024)]
    public async Task<IActionResult> Resubmit(DriverApplicationInput request)
    {
        var result = await drivers.ResubmitAsync(CurrentUserId, request);
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, data = await View(result.Value!) })
            : Error(result.StatusCode, result.Message);
    }

    // The driver's own profile picture (never their licence or NID images, which only admins see).
    [Authorize(Roles = UserRoles.Driver)]
    [HttpGet("profile-photo")]
    public async Task<IActionResult> ProfilePhoto([FromServices] DriverVerificationService verification)
    {
        var stored = await verification.DocumentAsync(CurrentUserId, DriverDocumentTypes.ProfilePhoto);
        if (stored is null)
        {
            return NotFoundError("No profile picture.");
        }
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(stored.Content, stored.ContentType);
    }

    private async Task<DriverApplicationViewModel> View(DriverProfile profile) =>
        DriverApplicationViewModel.From(profile, await drivers.AssignmentsOfAsync(profile.UserId), forAdmin: false, DriverRules.Today(DateTime.UtcNow));
}
