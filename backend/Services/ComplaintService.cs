using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

public record ComplaintFilter(string? Search = null, string? Category = null, string? Status = null, DateOnly? From = null, DateOnly? To = null,
    bool Oldest = false, int Page = 1, int PageSize = 15);

public record ComplaintStats(int Total, int Pending, int Reviewing, int Resolved, int Rejected, List<(string Category, int Count)> ByCategory,
    List<(string Date, int Count)> Last30Days, List<(string Bus, int Count)> TopBuses);

// F18: complaints about Dhaka buses. The person complaining always comes from the sign-in token. The bus must be one of
// the Dhaka routes in the database (and the stop, if one is given, a stop on it); its name and route are copied from
// there. New complaints start PENDING on the server, only admins move them on (along ComplaintStatus.Next, always
// with a note), and every step is written to complaint_history in the same save as the change. Evidence pictures are
// checked like the other uploads and kept as files that only the owner and admins can fetch.
public class ComplaintService(AppDbContext db, BusRouteCache routes, IWebHostEnvironment env, IOptions<ComplaintOptions> options,
    NotificationService notifications)
{
    public const int MaxDaysBack = 365;

    private string Folder => Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.StoragePath));

    private IQueryable<Complaint> Full => db.Complaints.Include(c => c.User).Include(c => c.Evidence)
        .Include(c => c.History).ThenInclude(h => h.Actor).AsSplitQuery();

    // ---- Reading ----

    public Task<List<Complaint>> MineAsync(Guid userId) => db.Complaints.Include(c => c.Evidence).AsNoTracking()
        .Where(c => c.UserId == userId).OrderByDescending(c => c.CreatedAt).ToListAsync();

    // Only the person who complained, or an admin. Someone else's complaint is answered as not found, so ids
    // cannot be probed.
    public async Task<Complaint?> FindAsync(Guid id, Guid viewerId, bool isAdmin)
    {
        var complaint = await Full.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        return complaint is not null && (isAdmin || complaint.UserId == viewerId) ? complaint : null;
    }

    public async Task<(List<Complaint> Items, int Total)> AllAsync(ComplaintFilter f)
    {
        var query = db.Complaints.AsQueryable();
        if (f.Status is { Length: > 0 } status) query = query.Where(c => c.Status == status);
        if (f.Category is { Length: > 0 } category) query = query.Where(c => c.Category == category);
        if (f.From is { } from) query = query.Where(c => c.IncidentDate >= from);
        if (f.To is { } to) query = query.Where(c => c.IncidentDate <= to);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var text = f.Search.Trim();
            var code = text.StartsWith("CMP-", StringComparison.OrdinalIgnoreCase) ? text[4..] : text;
            var like = $"%{text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            var idPrefix = $"{code.ToLowerInvariant().Replace("\\", "").Replace("%", "").Replace("_", "")}%";
            query = query.Where(c => EF.Functions.ILike(c.Id.ToString(), idPrefix) || EF.Functions.ILike(c.BusName, like)
                || EF.Functions.ILike(c.Subject, like) || EF.Functions.ILike(c.RouteName, like)
                || EF.Functions.ILike(c.User!.Name, like) || EF.Functions.ILike(c.User.Email, like));
        }
        var ordered = f.Oldest ? query.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id) : query.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id);
        var items = await ordered.Include(c => c.User).Include(c => c.Evidence).Skip((f.Page - 1) * f.PageSize).Take(f.PageSize)
            .AsNoTracking().AsSingleQuery().ToListAsync();
        var total = f.Page == 1 && items.Count < f.PageSize ? items.Count : await query.CountAsync();
        return (items, total);
    }

    public async Task<List<ComplaintHistory>?> HistoryAsync(Guid id, Guid viewerId, bool isAdmin)
    {
        var owner = await db.Complaints.AsNoTracking().Where(c => c.Id == id).Select(c => (Guid?)c.UserId).FirstOrDefaultAsync();
        if (owner is null || !isAdmin && owner != viewerId)
        {
            return null;
        }
        return await db.ComplaintHistory.Include(h => h.Actor).AsNoTracking().Where(h => h.ComplaintId == id).OrderBy(h => h.CreatedAt).ToListAsync();
    }

    public async Task<ComplaintStats> StatsAsync(DateTime nowUtc)
    {
        var byStatus = await db.Complaints.AsNoTracking().GroupBy(c => c.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        int Count(string s) => byStatus.FirstOrDefault(x => x.Key == s)?.Count ?? 0;
        var byCategory = await db.Complaints.AsNoTracking().GroupBy(c => c.Category).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var since = nowUtc.AddDays(-30);
        var recent = await db.Complaints.AsNoTracking().Where(c => c.CreatedAt >= since).Select(c => c.CreatedAt).ToListAsync();
        var today = DhakaTime.FromUtc(nowUtc).Date;
        var days = Enumerable.Range(0, 30).Select(i => today.AddDays(i - 29))
            .Select(d => (d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), recent.Count(c => DhakaTime.FromUtc(c).Date == d))).ToList();
        var topBuses = await db.Complaints.AsNoTracking().GroupBy(c => c.BusName).Select(g => new { g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(5).ToListAsync();
        return new ComplaintStats(byStatus.Sum(x => x.Count), Count(ComplaintStatus.Pending), Count(ComplaintStatus.Reviewing), Count(ComplaintStatus.Resolved),
            Count(ComplaintStatus.Rejected),
            ComplaintCategories.All.Select(c => (c, byCategory.FirstOrDefault(x => x.Key == c)?.Count ?? 0)).ToList(),
            days, topBuses.Select(x => (x.Key, x.Count)).ToList());
    }

    public StoredImage? OpenEvidence(ComplaintEvidence evidence) => ImageStorageService.OpenFile(Folder, evidence.StorageKey, evidence.ContentType);

    // The picture, if the viewer may see the complaint it belongs to.
    public async Task<StoredImage?> EvidenceAsync(Guid complaintId, Guid evidenceId, Guid viewerId, bool isAdmin)
    {
        var item = await db.ComplaintEvidence.AsNoTracking().Include(e => e.Complaint)
            .FirstOrDefaultAsync(e => e.Id == evidenceId && e.ComplaintId == complaintId);
        return item is null || !isAdmin && item.Complaint!.UserId != viewerId ? null : OpenEvidence(item);
    }

    // ---- Creating ----

    public async Task<ServiceResult<Complaint>> CreateAsync(Guid userId, CreateComplaintRequest r)
    {
        var now = DateTime.UtcNow;
        var bus = r.BusId is { } busId ? await routes.ByIdAsync(busId) : null;
        if (bus is null)
        {
            return ServiceResult<Complaint>.BadRequest("Choose a Dhaka bus and route from the list.");
        }
        var category = ComplaintCategories.All.FirstOrDefault(c => string.Equals(c, r.Category?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (category is null)
        {
            return ServiceResult<Complaint>.BadRequest("Choose one of the complaint categories.");
        }
        string? stop = null;
        if (!string.IsNullOrWhiteSpace(r.StopName))
        {
            stop = bus.Stops.FirstOrDefault(s => BusRouteMatcher.Normalize(s.Name) == BusRouteMatcher.Normalize(r.StopName))?.Name;
            if (stop is null)
            {
                return ServiceResult<Complaint>.BadRequest($"{bus.BusName} does not stop at \"{r.StopName.Trim()}\". Choose a stop on this route.");
            }
        }
        if (!DateOnly.TryParseExact(r.IncidentDate?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return ServiceResult<Complaint>.BadRequest("Enter the date like 2026-11-09.");
        }
        var today = DateOnly.FromDateTime(DhakaTime.FromUtc(now));
        if (date > today)
        {
            return ServiceResult<Complaint>.BadRequest("The incident date cannot be in the future.");
        }
        if (date < today.AddDays(-MaxDaysBack))
        {
            return ServiceResult<Complaint>.BadRequest("Complaints can only be made about the last 12 months.");
        }
        string Clean(string? text) => string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var subject = Clean(r.Subject);
        var description = (r.Description ?? "").Trim();
        var location = Clean(r.IncidentLocation);
        if (subject.Length is < 5 or > 150) return ServiceResult<Complaint>.BadRequest("The subject must be 5 to 150 characters.");
        if (description.Length is < 20 or > 2000) return ServiceResult<Complaint>.BadRequest("The description must be 20 to 2000 characters.");
        if (location.Length is < 2 or > 200) return ServiceResult<Complaint>.BadRequest("The location must be 2 to 200 characters.");
        if (r.Latitude is { } lat && r.Longitude is { } lng && !DhakaCityArea.Default.InCity(lat, lng))
        {
            return ServiceResult<Complaint>.BadRequest("The location must be inside Dhaka city.");
        }
        var pictures = (r.Evidence ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (pictures.Count > options.Value.MaxImages)
        {
            return ServiceResult<Complaint>.BadRequest($"Add at most {options.Value.MaxImages} pictures.");
        }
        // The same complaint sent twice in a row (a double click, a retry) is saved once.
        var since = now.AddMinutes(-2);
        if (await db.Complaints.AnyAsync(c => c.UserId == userId && c.BusId == busIdOf(bus) && c.Category == category && c.Subject == subject && c.CreatedAt > since))
        {
            return ServiceResult<Complaint>.Conflict("You just submitted this complaint. Find it under My complaints.");
        }

        // Check every picture before saving any.
        var decoded = new List<DecodedImage>();
        foreach (var picture in pictures)
        {
            try
            {
                decoded.Add(ImageStorageService.Decode(picture, options.Value.MaxImageBytes));
            }
            catch (ArgumentException e)
            {
                return ServiceResult<Complaint>.BadRequest(e.Message);
            }
        }
        var complaint = new Complaint
        {
            UserId = userId, BusId = busIdOf(bus), BusName = Cut(bus.BusName, 100), RouteNumber = bus.RouteNumber, RouteName = RouteText(bus), StopName = stop,
            Category = category, Subject = subject, Description = description, IncidentDate = date, IncidentLocation = location,
            Latitude = r.Latitude, Longitude = r.Longitude, Status = ComplaintStatus.Pending, CreatedAt = now, UpdatedAt = now,
        };
        complaint.History.Add(new ComplaintHistory { ToStatus = ComplaintStatus.Pending, ActorId = userId, ActorRole = "user", Note = "Complaint submitted.", CreatedAt = now });
        var saved = new List<string>();
        try
        {
            Directory.CreateDirectory(Folder);
            foreach (var image in decoded)
            {
                var key = $"{Guid.NewGuid():N}{image.Extension}";
                await using (var file = new FileStream(Path.Combine(Folder, key), FileMode.CreateNew, FileAccess.Write))
                {
                    await file.WriteAsync(image.Bytes.AsMemory(0, image.Length));
                }
                saved.Add(key);
                complaint.Evidence.Add(new ComplaintEvidence { StorageKey = key, ContentType = image.ContentType, SizeBytes = image.Length, CreatedAt = now });
            }
            db.Complaints.Add(complaint);
            await db.SaveChangesAsync();
        }
        catch
        {
            DeleteFiles(saved);
            throw;
        }
        return ServiceResult<Complaint>.Created((await FindAsync(complaint.Id, userId, false))!,
            $"Thank you. Your complaint {ComplaintViewModel.CodeOf(complaint.Id)} was submitted and is pending review.");
    }

    private static Guid? busIdOf(BusViewModel bus) => Guid.TryParse(bus.Id, out var id) ? id : null;

    private static string Cut(string text, int max) => text.Length > max ? text[..max] : text;

    private static string RouteText(BusViewModel bus)
    {
        var stops = bus.Stops;
        var ends = stops.Count >= 2 ? $"{stops[0].Name} → {stops[^1].Name}" : "";
        var text = string.IsNullOrWhiteSpace(bus.RouteNumber) ? ends : ends.Length == 0 ? $"Route {bus.RouteNumber}" : $"Route {bus.RouteNumber}: {ends}";
        return Cut(text, 160);
    }

    private void DeleteFiles(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            try
            {
                File.Delete(Path.Combine(Folder, key));
            }
            catch (IOException)
            {
                // A file left behind can no longer be reached from any complaint.
            }
        }
    }

    // ---- Changing ----

    // Only the person who complained, and only while it is still pending (nobody has started looking at it).
    public async Task<ServiceResult<bool>> DeleteAsync(Guid userId, Guid id)
    {
        var complaint = await db.Complaints.Include(c => c.Evidence).FirstOrDefaultAsync(c => c.Id == id);
        if (complaint is null || complaint.UserId != userId)
        {
            return ServiceResult<bool>.NotFound("Complaint not found.");
        }
        if (complaint.Status != ComplaintStatus.Pending)
        {
            return ServiceResult<bool>.Conflict("Only pending complaints can be deleted. This one is already being handled.");
        }
        var keys = complaint.Evidence.Select(e => e.StorageKey).ToList();
        db.Complaints.Remove(complaint);
        await db.SaveChangesAsync();
        DeleteFiles(keys);
        return ServiceResult<bool>.Ok(true, "Complaint deleted.");
    }

    // Admins only (checked by the controller). Moves the complaint along ComplaintStatus.Next with a note, records it
    // in the history, and tells the person who complained.
    public async Task<ServiceResult<Complaint>> SetStatusAsync(Guid adminId, Guid id, string status, string? note)
    {
        status = (status ?? "").Trim().ToUpperInvariant();
        note = note?.Trim() ?? "";
        if (!ComplaintStatus.All.Contains(status))
        {
            return ServiceResult<Complaint>.BadRequest("Status must be PENDING, REVIEWING, RESOLVED or REJECTED.");
        }
        if (note.Length is < 5 or > 1000)
        {
            return ServiceResult<Complaint>.BadRequest("Write a note of 5 to 1000 characters for the user.");
        }
        var complaint = await db.Complaints.FirstOrDefaultAsync(c => c.Id == id);
        if (complaint is null)
        {
            return ServiceResult<Complaint>.NotFound("Complaint not found.");
        }
        if (!ComplaintStatus.CanMove(complaint.Status, status))
        {
            return ServiceResult<Complaint>.Conflict(complaint.Status == status
                ? $"The complaint is already {status}."
                : $"A {complaint.Status} complaint cannot be moved to {status}.");
        }
        var now = DateTime.UtcNow;
        db.ComplaintHistory.Add(new ComplaintHistory { ComplaintId = id, FromStatus = complaint.Status, ToStatus = status, Note = note, ActorId = adminId, ActorRole = "admin", CreatedAt = now });
        complaint.Status = status;
        complaint.AdminNotes = note;
        complaint.ReviewedById = adminId;
        complaint.UpdatedAt = now;
        complaint.ResolvedAt = status is ComplaintStatus.Resolved or ComplaintStatus.Rejected ? now : null;
        var label = status switch
        {
            ComplaintStatus.Reviewing => "is being reviewed",
            ComplaintStatus.Resolved => "was resolved",
            ComplaintStatus.Rejected => "was rejected",
            _ => "is pending again",
        };
        notifications.Notify(complaint.UserId, $"Complaint {ComplaintViewModel.CodeOf(id)} {label}", $"\"{Cut(complaint.Subject, 80)}\" {label}. Admin note: {Cut(note, 300)}",
            status == ComplaintStatus.Rejected ? NotificationTypes.Warning : status == ComplaintStatus.Resolved ? NotificationTypes.Success : NotificationTypes.Info,
            "/complaints", NotificationCategories.General, adminId, id);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return ServiceResult<Complaint>.Conflict("Another admin changed this complaint at the same moment. Reload it and try again.");
        }
        return ServiceResult<Complaint>.Ok((await FindAsync(id, adminId, true))!, $"Complaint marked {status}.");
    }
}
