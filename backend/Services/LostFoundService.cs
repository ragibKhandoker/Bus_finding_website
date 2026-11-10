using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

public record LostFoundFilter(string? Type = null, string? Category = null, string? Search = null, string? Status = null, int Page = 1, int PageSize = 12);

// F17: Lost & Found reports for Dhaka buses. The person making a report always comes from the sign-in token; only
// they (or an admin) may change or delete it. The bus must be one of the Dhaka routes in the database, and its name
// and route are copied from there, never from the client. Pictures are checked like the other uploads
// (ImageStorageService.Decode: JPEG, PNG or WebP, the real format, at most LostFound:MaxImageBytes) and kept as files
// with random names; the database only stores that name.
public class LostFoundService(AppDbContext db, BusRouteCache routes, IWebHostEnvironment env, IOptions<LostFoundOptions> options,
    NotificationService notifications)
{
    // A report from long ago is not useful to anyone; one from the future is a typo.
    public const int MaxDaysBack = 365;

    private string Folder => Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.StoragePath));

    public static string ImageUrl(string key) => $"/api/lostfound/images/{key}";

    public StoredImage? OpenImage(string key)
    {
        var type = Path.GetExtension(key) switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
        return ImageStorageService.OpenFile(Folder, key, type);
    }

    private IQueryable<LostFound> WithUser => db.LostFoundReports.Include(r => r.User).AsNoTracking();

    // ---- Reading ----

    // Reports everyone signed in can see. Closed reports are left out unless asked for by status.
    public async Task<(List<LostFound> Items, int Total)> ListAsync(LostFoundFilter f)
    {
        var query = db.LostFoundReports.AsQueryable();
        if (f.Type is { Length: > 0 } type) query = query.Where(r => r.ReportType == type);
        if (f.Category is { Length: > 0 } category) query = query.Where(r => r.Category == category);
        query = f.Status is { Length: > 0 } status ? query.Where(r => r.Status == status) : query.Where(r => r.Status != LostFoundStatus.Closed);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var term = $"%{f.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            query = query.Where(r => EF.Functions.ILike(r.ItemName, term) || EF.Functions.ILike(r.Description, term) || EF.Functions.ILike(r.BusName, term)
                || EF.Functions.ILike(r.Route, term) || EF.Functions.ILike(r.Location, term) || EF.Functions.ILike(r.Category, term));
        }
        var items = await query.Include(r => r.User).OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).AsNoTracking().ToListAsync();
        var total = f.Page == 1 && items.Count < f.PageSize ? items.Count : await query.CountAsync();
        return (items, total);
    }

    public Task<List<LostFound>> MineAsync(Guid userId) =>
        WithUser.Where(r => r.UserId == userId).OrderByDescending(r => r.CreatedAt).ToListAsync();

    public Task<LostFound?> FindAsync(Guid id) => WithUser.FirstOrDefaultAsync(r => r.Id == id);

    // Reports of the other kind that may be the same item: same category, still open, within a week of each other.
    // Reports on the same bus come first.
    public async Task<List<LostFound>> MatchesAsync(LostFound report)
    {
        var other = report.ReportType == LostFoundTypes.Lost ? LostFoundTypes.Found : LostFoundTypes.Lost;
        var from = report.IncidentDate.AddDays(-7);
        var to = report.IncidentDate.AddDays(7);
        var list = await WithUser
            .Where(r => r.ReportType == other && r.Category == report.Category && r.UserId != report.UserId
                && (r.Status == LostFoundStatus.Open || r.Status == LostFoundStatus.UnderReview)
                && r.IncidentDate >= from && r.IncidentDate <= to)
            .OrderByDescending(r => r.BusName == report.BusName).ThenByDescending(r => r.CreatedAt)
            .Take(5).ToListAsync();
        return list;
    }

    // ---- Checking ----

    private record Checked(string Type, string Item, string Category, BusViewModel Bus, string Location, DateOnly Date, TimeOnly? Time,
        string Description, string Contact);

    private async Task<(Checked? Value, string? Problem)> CheckAsync(LostFoundRequest r, DateTime nowUtc)
    {
        var type = r.ReportType.Trim().ToLowerInvariant();
        if (!LostFoundTypes.All.Contains(type))
        {
            return (null, "Choose Lost or Found.");
        }
        var category = LostFoundCategories.All.FirstOrDefault(c => string.Equals(c, r.Category.Trim(), StringComparison.OrdinalIgnoreCase));
        if (category is null)
        {
            return (null, $"Choose a category: {string.Join(", ", LostFoundCategories.All)}.");
        }
        var bus = r.BusId is { } busId ? await routes.ByIdAsync(busId) : null;
        if (bus is null)
        {
            return (null, "Choose a Dhaka bus from the list.");
        }
        if (!DateOnly.TryParseExact(r.IncidentDate?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return (null, "Enter the date like 2026-11-09.");
        }
        var today = DateOnly.FromDateTime(DhakaTime.FromUtc(nowUtc));
        if (date > today)
        {
            return (null, "The date cannot be in the future.");
        }
        if (date < today.AddDays(-MaxDaysBack))
        {
            return (null, "Reports can only be made for the last 12 months.");
        }
        TimeOnly? time = null;
        if (!string.IsNullOrWhiteSpace(r.IncidentTime))
        {
            if (!TimeOnly.TryParseExact(r.IncidentTime.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return (null, "Enter the time like 18:30.");
            }
            if (date == today && parsed > TimeOnly.FromDateTime(DhakaTime.FromUtc(nowUtc)).Add(TimeSpan.FromMinutes(5)))
            {
                return (null, "The time cannot be in the future.");
            }
            time = parsed;
        }
        string Clean(string? text) => string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var item = Clean(r.ItemName);
        var location = Clean(r.Location);
        var description = (r.Description ?? "").Trim();
        var contact = Clean(r.ContactInformation);
        if (item.Length is < 2 or > 100) return (null, "The item name must be 2 to 100 characters.");
        if (location.Length is < 2 or > 200) return (null, "The location must be 2 to 200 characters.");
        if (description.Length is < 10 or > 1000) return (null, "The description must be 10 to 1000 characters.");
        if (contact.Length is < 5 or > 150) return (null, "The contact information must be 5 to 150 characters.");
        return (new Checked(type, item, category, bus, location, date, time, description, contact), null);
    }

    private static string RouteText(BusViewModel bus)
    {
        var stops = bus.Stops;
        var ends = stops.Count >= 2 ? $"{stops[0].Name} → {stops[^1].Name}" : "";
        var text = string.IsNullOrWhiteSpace(bus.RouteNumber) ? ends : ends.Length == 0 ? $"Route {bus.RouteNumber}" : $"Route {bus.RouteNumber}: {ends}";
        return text.Length > 160 ? text[..160] : text;
    }

    private static void Apply(LostFound report, Checked c)
    {
        report.ReportType = c.Type;
        report.ItemName = c.Item;
        report.Category = c.Category;
        report.BusId = Guid.TryParse(c.Bus.Id, out var id) ? id : null;
        report.BusName = c.Bus.BusName.Length > 100 ? c.Bus.BusName[..100] : c.Bus.BusName;
        report.Route = RouteText(c.Bus);
        report.Location = c.Location;
        report.IncidentDate = c.Date;
        report.IncidentTime = c.Time;
        report.Description = c.Description;
        report.ContactInformation = c.Contact;
    }

    // Saves a picture sent as a data URL and returns its file name. Throws ArgumentException with a message for the
    // user when the picture is refused.
    public async Task<(string Key, string ContentType)> SaveImageAsync(string dataUrl)
    {
        var image = ImageStorageService.Decode(dataUrl, options.Value.MaxImageBytes);
        Directory.CreateDirectory(Folder);
        var key = $"{Guid.NewGuid():N}{image.Extension}";
        await using var file = new FileStream(Path.Combine(Folder, key), FileMode.CreateNew, FileAccess.Write);
        await file.WriteAsync(image.Bytes.AsMemory(0, image.Length));
        return (key, image.ContentType);
    }

    public void DeleteImage(string? key)
    {
        if (key is null || OpenImage(key) is not { } stored)
        {
            return;
        }
        stored.Content.Dispose();
        try
        {
            File.Delete(Path.Combine(Folder, key));
        }
        catch (IOException)
        {
            // A file left behind does no harm; it can no longer be reached from any report.
        }
    }

    // ---- Changing ----

    public async Task<ServiceResult<LostFound>> CreateAsync(Guid userId, LostFoundRequest request)
    {
        var now = DateTime.UtcNow;
        var (c, problem) = await CheckAsync(request, now);
        if (c is null)
        {
            return ServiceResult<LostFound>.BadRequest(problem!);
        }
        // The same report sent twice in a row (a double click, a retry) is saved once.
        var since = now.AddMinutes(-2);
        if (await db.LostFoundReports.AnyAsync(r => r.UserId == userId && r.ReportType == c.Type && r.ItemName == c.Item
                && r.IncidentDate == c.Date && r.CreatedAt > since))
        {
            return ServiceResult<LostFound>.Conflict("You just submitted this report. Find it under My reports.");
        }
        var report = new LostFound { UserId = userId, Status = LostFoundStatus.Open, CreatedAt = now, UpdatedAt = now };
        Apply(report, c);
        if (!string.IsNullOrWhiteSpace(request.Image))
        {
            try
            {
                (report.ImageKey, report.ImageContentType) = await SaveImageAsync(request.Image);
            }
            catch (ArgumentException e)
            {
                return ServiceResult<LostFound>.BadRequest(e.Message);
            }
        }
        db.LostFoundReports.Add(report);
        try
        {
            await db.SaveChangesAsync();
        }
        catch
        {
            DeleteImage(report.ImageKey);
            throw;
        }
        return ServiceResult<LostFound>.Created((await FindAsync(report.Id))!,
            report.ReportType == LostFoundTypes.Lost ? "Your lost item report is posted. We hope you get it back soon!" : "Thank you! Your found item report is posted.");
    }

    // Only the person who made the report, or an admin. Someone else's report is answered as not found, so its id
    // cannot be probed.
    private async Task<(LostFound? Report, ServiceResult<T>? Failure)> OwnAsync<T>(Guid userId, bool isAdmin, Guid id)
    {
        var report = await db.LostFoundReports.FirstOrDefaultAsync(r => r.Id == id);
        if (report is null)
        {
            return (null, ServiceResult<T>.NotFound("Report not found."));
        }
        if (report.UserId != userId && !isAdmin)
        {
            return (null, ServiceResult<T>.Forbidden("You can only change your own reports."));
        }
        return (report, null);
    }

    public async Task<ServiceResult<LostFound>> UpdateAsync(Guid userId, bool isAdmin, Guid id, LostFoundRequest request)
    {
        var (report, failure) = await OwnAsync<LostFound>(userId, isAdmin, id);
        if (report is null)
        {
            return failure!;
        }
        var now = DateTime.UtcNow;
        var (c, problem) = await CheckAsync(request, now);
        if (c is null)
        {
            return ServiceResult<LostFound>.BadRequest(problem!);
        }
        string? status = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            status = request.Status.Trim().ToLowerInvariant();
            var allowed = isAdmin ? LostFoundStatus.All : LostFoundStatus.ForOwner;
            if (!allowed.Contains(status))
            {
                return ServiceResult<LostFound>.BadRequest(isAdmin ? "Unknown status." : "You can set your report to open, resolved or closed.");
            }
        }
        string? newKey = null, newType = null;
        if (!string.IsNullOrWhiteSpace(request.Image))
        {
            try
            {
                (newKey, newType) = await SaveImageAsync(request.Image);
            }
            catch (ArgumentException e)
            {
                return ServiceResult<LostFound>.BadRequest(e.Message);
            }
        }
        var oldKey = report.ImageKey;
        Apply(report, c);
        if (newKey is not null)
        {
            report.ImageKey = newKey;
            report.ImageContentType = newType;
        }
        else if (request.RemoveImage)
        {
            report.ImageKey = null;
            report.ImageContentType = null;
        }
        if (status is not null && status != report.Status)
        {
            report.Status = status;
            report.ResolvedAt = status == LostFoundStatus.Resolved ? now : null;
        }
        report.UpdatedAt = now;
        try
        {
            await db.SaveChangesAsync();
        }
        catch
        {
            DeleteImage(newKey);
            throw;
        }
        if (oldKey is not null && oldKey != report.ImageKey)
        {
            DeleteImage(oldKey);
        }
        return ServiceResult<LostFound>.Ok((await FindAsync(id))!, "Report updated.");
    }

    public async Task<ServiceResult<bool>> DeleteAsync(Guid userId, bool isAdmin, Guid id)
    {
        var (report, failure) = await OwnAsync<bool>(userId, isAdmin, id);
        if (report is null)
        {
            return failure!;
        }
        var key = report.ImageKey;
        db.LostFoundReports.Remove(report);
        await db.SaveChangesAsync();
        DeleteImage(key);
        return ServiceResult<bool>.Ok(true, "Report deleted.");
    }

    // Admins review reports; the person who made it is told when the status changes.
    public async Task<ServiceResult<LostFound>> SetStatusAsync(Guid adminId, Guid id, string status, string? note)
    {
        status = status.Trim().ToLowerInvariant();
        if (!LostFoundStatus.All.Contains(status))
        {
            return ServiceResult<LostFound>.BadRequest("Status must be open, under-review, resolved or closed.");
        }
        var report = await db.LostFoundReports.FirstOrDefaultAsync(r => r.Id == id);
        if (report is null)
        {
            return ServiceResult<LostFound>.NotFound("Report not found.");
        }
        var now = DateTime.UtcNow;
        report.Status = status;
        report.AdminNote = string.IsNullOrWhiteSpace(note) ? report.AdminNote : note.Trim()[..Math.Min(300, note.Trim().Length)];
        report.ResolvedAt = status == LostFoundStatus.Resolved ? now : null;
        report.UpdatedAt = now;
        if (report.UserId != adminId)
        {
            var label = status switch { LostFoundStatus.UnderReview => "under review", _ => status };
            notifications.Notify(report.UserId, "Lost & Found report updated", $"Your report \"{report.ItemName}\" is now {label}.{(string.IsNullOrWhiteSpace(note) ? "" : $" Note: {note.Trim()}")}",
                NotificationTypes.Info, "/lost-found", NotificationCategories.General, adminId, report.Id);
        }
        await db.SaveChangesAsync();
        return ServiceResult<LostFound>.Ok((await FindAsync(id))!, "Status updated.");
    }
}
