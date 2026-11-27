using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// F19: the signed-in commuter's travel preferences (many per user). Everything needs a sign-in and works only on
// the caller's own preferences, taken from the token; a user id in the body is ignored. Someone else's preference
// is answered 404, like one that does not exist. The rules live in CommuterProfileService.
[Authorize]
[Route("api/commuter-profile")]
public class CommuterProfileController(CommuterProfileService preferences) : ApiControllerBase
{
    // The choices the form offers, so the page and the server use the same lists.
    [HttpGet("options")]
    public IActionResult Options() => Ok(new
    {
        success = true,
        busTypes = TravelBusTypes.All,
        travelPurposes = TravelPurposes.All,
        travelDays = TravelWeekDays.All,
        maxPreferences = TravelPreference.MaxPerUser,
    });

    [HttpGet("my")]
    public async Task<IActionResult> My(CancellationToken ct)
    {
        var data = (await preferences.MineAsync(CurrentUserId, ct)).Select(TravelPreferenceViewModel.From).ToList();
        return Ok(new { success = true, count = data.Count, maxPreferences = TravelPreference.MaxPerUser, data });
    }

    [HttpGet("my/count")]
    public async Task<IActionResult> Count(CancellationToken ct) =>
        Ok(new { success = true, count = await preferences.CountAsync(CurrentUserId, ct), maxPreferences = TravelPreference.MaxPerUser });

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var preference = await preferences.FindAsync(CurrentUserId, id, ct);
        return preference is null
            ? NotFoundError("Travel preference not found.")
            : Ok(new { success = true, data = TravelPreferenceViewModel.From(preference) });
    }

    [HttpPost]
    public async Task<IActionResult> Create(TravelPreferenceRequest request, CancellationToken ct)
    {
        var result = await preferences.CreateAsync(CurrentUserId, request, ct);
        return result.Succeeded
            ? Created201(new { success = true, message = result.Message, data = TravelPreferenceViewModel.From(result.Value!) })
            : Error(result.StatusCode, result.Message);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, TravelPreferenceRequest request, CancellationToken ct)
    {
        var result = await preferences.UpdateAsync(CurrentUserId, id, request, ct);
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, data = TravelPreferenceViewModel.From(result.Value!) })
            : Error(result.StatusCode, result.Message);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await preferences.DeleteAsync(CurrentUserId, id, ct)
            ? Ok(new { success = true, message = "Travel preference deleted." })
            : NotFoundError("Travel preference not found.");
}
