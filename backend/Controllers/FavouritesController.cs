using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// The Smart Favourite Bus module for Dhaka City buses. Every action is for the signed-in user, taken from the
// token (a user id in a request is never trusted), and every bus is checked server-side to be a Dhaka City bus.
// /api/favbus (FavBusController) is the older address of the add/remove/list/check actions and shares their rules.
[Authorize]
[Route("api/favourites")]
public class FavouritesController(
    FavoriteBusService favorites,
    FavouriteInsightsService insights,
    BusAlertService alerts) : ApiControllerBase
{
    private IActionResult From<T>(ServiceResult<T> result, Func<T, object> body) =>
        result.Succeeded ? StatusCode(result.StatusCode, body(result.Value!)) : Error(result.StatusCode, result.Message);

    private ObjectResult InvalidId(string what = "bus") => BadRequestError($"The {what} id is not valid.");

    // ---------------- Favourites ----------------

    [HttpPost]
    public async Task<IActionResult> Add(FavouriteBusIdRequest request, CancellationToken ct)
    {
        var busId = Ids.Parse(request.BusId);
        if (busId == Guid.Empty) return InvalidId();
        var result = await favorites.AddAsync(CurrentUserId, busId, ct);
        if (!result.Succeeded) return Error(result.StatusCode, result.Message);
        var (favorite, created) = result.Value;
        var body = new { success = true, message = result.Message, isFavourite = true, created, data = favorite };
        return created ? Created201(body) : Ok(body);
    }

    [HttpDelete("{busId:guid}")]
    public async Task<IActionResult> Remove(Guid busId, CancellationToken ct)
    {
        var removed = await favorites.RemoveAsync(CurrentUserId, busId, ct);
        return Ok(new { success = true, message = removed ? "Bus removed from favourites." : "This bus was not in your favourites.", isFavourite = false, removed });
    }

    [HttpGet("my")]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {
        var data = await favorites.ForUserAsync(CurrentUserId, ct);
        return Ok(new { success = true, count = data.Count, data });
    }

    [HttpGet("check/{busId:guid}")]
    public async Task<IActionResult> Check(Guid busId, CancellationToken ct) =>
        Ok(new { success = true, busId, isFavourite = await favorites.IsFavoriteAsync(CurrentUserId, busId, ct) });

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken ct) =>
        Ok(new { success = true, data = await insights.SummaryAsync(CurrentUserId, ct) });

    // ---------------- Comparison ----------------

    [HttpPost("compare")]
    public async Task<IActionResult> Compare(FavouriteCompareRequest request, CancellationToken ct) =>
        From(await insights.CompareAsync(CurrentUserId, request, ct), data => new { success = true, data });

    // ---------------- Fare tracking ----------------

    [HttpGet("price-tracking")]
    public async Task<IActionResult> PriceTracking(CancellationToken ct) =>
        Ok(new { success = true, data = await insights.PriceTrackingAsync(CurrentUserId, ct) });

    [HttpGet("{busId:guid}/price-history")]
    public async Task<IActionResult> PriceHistory(Guid busId, CancellationToken ct) =>
        From(await insights.PriceHistoryAsync(CurrentUserId, busId, ct), data => new { success = true, busId, data });

    // ---------------- Availability alerts ----------------

    [HttpGet("availability-alerts")]
    public async Task<IActionResult> Alerts(CancellationToken ct) =>
        Ok(new
        {
            success = true,
            data = await alerts.AlertsAsync(CurrentUserId, ct),
            // The app has no seat booking or timetable; see the note in BusAvailabilityAlert.
            source = "Free seats on the route's buses in service, from riders boarding and leaving through the app. Travel dates need a seat-booking system, which the app does not have.",
        });

    [HttpPost("availability-alerts")]
    public async Task<IActionResult> CreateAlert(AvailabilityAlertRequest request, CancellationToken ct) =>
        From(await alerts.CreateAlertAsync(CurrentUserId, request, ct), data => new { success = true, message = "Availability alert saved.", data });

    [HttpPut("availability-alerts/{id:guid}")]
    public async Task<IActionResult> UpdateAlert(Guid id, AvailabilityAlertRequest request, CancellationToken ct) =>
        From(await alerts.UpdateAlertAsync(CurrentUserId, id, request, ct), data => new { success = true, message = "Availability alert updated.", data });

    [HttpDelete("availability-alerts/{id:guid}")]
    public async Task<IActionResult> DeleteAlert(Guid id, CancellationToken ct) =>
        await alerts.DeleteAlertAsync(CurrentUserId, id, ct)
            ? Ok(new { success = true, message = "Availability alert deleted." })
            : NotFoundError("Alert not found.");

    // ---------------- Departure reminders (Bangladesh Standard Time) ----------------

    [HttpGet("reminders")]
    public async Task<IActionResult> Reminders(CancellationToken ct) =>
        Ok(new { success = true, timeZone = "Asia/Dhaka", data = await alerts.RemindersAsync(CurrentUserId, ct) });

    [HttpPost("reminders")]
    public async Task<IActionResult> CreateReminder(BusReminderRequest request, CancellationToken ct) =>
        From(await alerts.CreateReminderAsync(CurrentUserId, request, ct), data => new { success = true, message = "Reminder saved.", data });

    [HttpPut("reminders/{id:guid}")]
    public async Task<IActionResult> UpdateReminder(Guid id, BusReminderRequest request, CancellationToken ct) =>
        From(await alerts.UpdateReminderAsync(CurrentUserId, id, request, ct), data => new { success = true, message = "Reminder updated.", data });

    [HttpDelete("reminders/{id:guid}")]
    public async Task<IActionResult> DeleteReminder(Guid id, CancellationToken ct) =>
        await alerts.DeleteReminderAsync(CurrentUserId, id, ct)
            ? Ok(new { success = true, message = "Reminder deleted." })
            : NotFoundError("Reminder not found.");

    // ---------------- Recently viewed ----------------

    [HttpGet("recently-viewed")]
    public async Task<IActionResult> RecentlyViewed(CancellationToken ct) =>
        Ok(new { success = true, data = await insights.RecentlyViewedAsync(CurrentUserId, ct) });

    [HttpPost("recently-viewed")]
    public async Task<IActionResult> RecordView(FavouriteBusIdRequest request, CancellationToken ct)
    {
        var busId = Ids.Parse(request.BusId);
        if (busId == Guid.Empty) return InvalidId();
        return From(await insights.RecordViewAsync(CurrentUserId, busId, ct), _ => new { success = true });
    }

    [HttpDelete("recently-viewed")]
    public async Task<IActionResult> ClearRecentlyViewed(CancellationToken ct) =>
        Ok(new { success = true, message = "Viewing history cleared.", count = await insights.ClearRecentlyViewedAsync(CurrentUserId, ct) });

    // ---------------- Notifications (this module's categories only) ----------------

    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] bool unreadOnly = false, [FromQuery] string? category = null, CancellationToken ct = default)
    {
        if (category is { Length: > 0 } && !NotificationCategories.Bus.Contains(category))
        {
            return BadRequestError("Unknown notification category.");
        }
        page = Math.Max(1, page);
        var size = Math.Clamp(pageSize, 1, 100);
        var (items, total, unread) = await insights.NotificationsAsync(CurrentUserId, page, size, unreadOnly, category, ct);
        return Ok(new
        {
            success = true,
            page,
            pageSize = size,
            total,
            totalPages = (int)Math.Ceiling(total / (double)size),
            unreadCount = unread,
            data = items.Select(NotificationViewModel.From),
        });
    }

    [HttpPut("notifications/{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct) =>
        await insights.MarkNotificationReadAsync(CurrentUserId, id, ct)
            ? Ok(new { success = true })
            : NotFoundError("Notification not found.");

    [HttpPut("notifications/read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct) =>
        Ok(new { success = true, count = await insights.MarkAllNotificationsReadAsync(CurrentUserId, ct), unreadCount = 0 });

    [HttpDelete("notifications/{id:guid}")]
    public async Task<IActionResult> DeleteNotification(Guid id, CancellationToken ct) =>
        await insights.DeleteNotificationAsync(CurrentUserId, id, ct)
            ? Ok(new { success = true, message = "Notification deleted." })
            : NotFoundError("Notification not found.");
}

// Collections of the signed-in user's favourite Dhaka City buses.
[Authorize]
[Route("api/favourite-collections")]
public class FavouriteCollectionsController(FavoriteCollectionService collections) : ApiControllerBase
{
    private IActionResult From(ServiceResult<FavouriteCollectionViewModel> result) =>
        result.Succeeded
            ? StatusCode(result.StatusCode, new { success = true, message = result.Message, data = result.Value })
            : Error(result.StatusCode, result.Message);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var data = await collections.ListAsync(CurrentUserId, ct);
        return Ok(new { success = true, count = data.Count, data });
    }

    [HttpPost]
    public async Task<IActionResult> Create(FavouriteCollectionRequest request, CancellationToken ct) =>
        From(await collections.CreateAsync(CurrentUserId, request.Name, ct));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Rename(Guid id, FavouriteCollectionRequest request, CancellationToken ct) =>
        From(await collections.RenameAsync(CurrentUserId, id, request.Name, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await collections.DeleteAsync(CurrentUserId, id, ct)
            ? Ok(new { success = true, message = "Collection deleted. Its buses are still in your favourites." })
            : NotFoundError("Collection not found.");

    [HttpPost("{id:guid}/buses")]
    public async Task<IActionResult> AddBus(Guid id, FavouriteCollectionBusRequest request, CancellationToken ct)
    {
        var busId = Ids.Parse(request.BusId);
        if (busId == Guid.Empty) return BadRequestError("The bus id is not valid.");
        Guid? from = null;
        if (!string.IsNullOrWhiteSpace(request.FromCollectionId))
        {
            var parsed = Ids.Parse(request.FromCollectionId);
            if (parsed == Guid.Empty) return BadRequestError("The collection id is not valid.");
            from = parsed;
        }
        return From(await collections.AddBusAsync(CurrentUserId, id, busId, from, ct));
    }

    [HttpDelete("{id:guid}/buses/{busId:guid}")]
    public async Task<IActionResult> RemoveBus(Guid id, Guid busId, CancellationToken ct) =>
        From(await collections.RemoveBusAsync(CurrentUserId, id, busId, ct));
}
