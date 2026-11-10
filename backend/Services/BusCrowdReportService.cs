using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

// ?bus= (bus name) · busId · stop (stop name) · crowdLevel · seatAvailability · date (Dhaka, yyyy-MM-dd) ·
// freshness=current|all|VERY_RECENT|RECENT|AGING|EXPIRED · sort=recent|least-crowded|most-crowded|seats.
public record CrowdReportFilter(string? Bus = null, Guid? BusId = null, string? Stop = null, string? CrowdLevel = null, string? SeatAvailability = null,
    DateOnly? Date = null, string Freshness = CrowdReportFilter.Current, string Sort = CrowdReportFilter.SortRecent, int Page = 1, int PageSize = 12)
{
    public const string Current = "current";
    public const string All = "all";
    public const string SortRecent = "recent";
    public const string SortLeastCrowded = "least-crowded";
    public const string SortMostCrowded = "most-crowded";
    public const string SortSeats = "seats";

    public static readonly string[] Freshnesses = [Current, All, .. CrowdFreshness.All];
    public static readonly string[] Sorts = [SortRecent, SortLeastCrowded, SortMostCrowded, SortSeats];
}

// F19: crowd and seat reports about Dhaka buses. The reporter always comes from the sign-in token. The route must be
// one of the Dhaka routes in the database and the stop one of its stops; names are copied from there. Freshness is
// worked out from the observation time, so an old report is never shown as the bus's current state. One person
// cannot report the same bus at the same stop twice for the same sighting (checked here and by a unique index), but
// any number of people can report the same bus.
public class BusCrowdReportService(AppDbContext db, BusRouteCache routes)
{
    public const int EditWindowMinutes = 15;
    public const int MaxHoursBack = 24;
    // A phone clock a little ahead of the server is not "the future".
    public const int FutureToleranceMinutes = 5;
    public const int DuplicateWindowMinutes = 10;
    public const int MaxComments = 500;

    // ---- Reading ----

    public Task<BusCrowdReport?> FindAsync(Guid id) => db.BusCrowdReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);

    public async Task<(List<BusCrowdReport> Items, int Total)> MineAsync(Guid userId, int page, int pageSize)
    {
        var query = db.BusCrowdReports.AsNoTracking().Where(r => r.UserId == userId);
        var items = await query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var total = page == 1 && items.Count < pageSize ? items.Count : await query.CountAsync();
        return (items, total);
    }

    public async Task<(List<BusCrowdReport> Items, int Total)> SearchAsync(CrowdReportFilter f, DateTime nowUtc)
    {
        var query = db.BusCrowdReports.AsNoTracking().AsQueryable();
        if (f.BusId is { } busId) query = query.Where(r => r.BusId == busId);
        if (!string.IsNullOrWhiteSpace(f.Bus))
        {
            var like = Like(f.Bus);
            query = query.Where(r => EF.Functions.ILike(r.BusName, like) || EF.Functions.ILike(r.RouteNumber!, like));
        }
        if (!string.IsNullOrWhiteSpace(f.Stop))
        {
            var like = Like(f.Stop);
            query = query.Where(r => EF.Functions.ILike(r.StopName, like));
        }
        if (f.CrowdLevel is { Length: > 0 } crowd) query = query.Where(r => r.CrowdLevel == crowd);
        if (f.SeatAvailability is { Length: > 0 } seats) query = query.Where(r => r.SeatAvailability == seats);
        if (f.Date is { } date)
        {
            var start = DhakaTime.ToUtc(date.ToDateTime(TimeOnly.MinValue));
            var end = start.AddDays(1);
            query = query.Where(r => r.ObservationDateTime >= start && r.ObservationDateTime < end);
        }
        var veryRecent = nowUtc.AddMinutes(-CrowdFreshness.VeryRecentMinutes);
        var recent = nowUtc.AddMinutes(-CrowdFreshness.RecentMinutes);
        var current = nowUtc.AddMinutes(-CrowdFreshness.CurrentMinutes);
        query = f.Freshness switch
        {
            CrowdReportFilter.Current => query.Where(r => r.ObservationDateTime >= current),
            CrowdFreshness.VeryRecent => query.Where(r => r.ObservationDateTime >= veryRecent),
            CrowdFreshness.Recent => query.Where(r => r.ObservationDateTime < veryRecent && r.ObservationDateTime >= recent),
            CrowdFreshness.Aging => query.Where(r => r.ObservationDateTime < recent && r.ObservationDateTime >= current),
            CrowdFreshness.Expired => query.Where(r => r.ObservationDateTime < current),
            _ => query,
        };

        // Ranks so "least crowded" and "seats available" sort in the order people mean, newest first within each.
        var ordered = f.Sort switch
        {
            CrowdReportFilter.SortLeastCrowded => query.OrderBy(r => r.CrowdLevel == CrowdLevels.Low ? 0 : r.CrowdLevel == CrowdLevels.Medium ? 1 : 2)
                .ThenByDescending(r => r.ObservationDateTime),
            CrowdReportFilter.SortMostCrowded => query.OrderBy(r => r.CrowdLevel == CrowdLevels.High ? 0 : r.CrowdLevel == CrowdLevels.Medium ? 1 : 2)
                .ThenByDescending(r => r.ObservationDateTime),
            CrowdReportFilter.SortSeats => query.OrderBy(r => r.SeatAvailability == SeatAvailabilities.Available ? 0 : r.SeatAvailability == SeatAvailabilities.Limited ? 1 : 2)
                .ThenByDescending(r => r.AvailableSeatCount ?? -1).ThenByDescending(r => r.ObservationDateTime),
            _ => query.OrderByDescending(r => r.ObservationDateTime),
        };
        var items = await ordered.ThenByDescending(r => r.Id).Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).ToListAsync();
        var total = f.Page == 1 && items.Count < f.PageSize ? items.Count : await query.CountAsync();
        return (items, total);
    }

    // Every route with at least one current report (seen in the last hour): its latest report and how many there are.
    // Expired reports never appear here.
    public async Task<List<(BusCrowdReport Latest, int Count)>> LiveAsync(DateTime nowUtc)
    {
        var since = nowUtc.AddMinutes(-CrowdFreshness.CurrentMinutes);
        var reports = await db.BusCrowdReports.AsNoTracking().Where(r => r.ObservationDateTime >= since)
            .OrderByDescending(r => r.ObservationDateTime).ThenByDescending(r => r.Id).Take(1000).ToListAsync();
        return reports.GroupBy(r => r.BusId).Select(g => (g.First(), g.Count())).OrderByDescending(x => x.Item1.ObservationDateTime).ToList();
    }

    private static string Like(string text) => $"%{text.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";

    // ---- Checking a form ----

    private record Checked(BusViewModel Bus, BusStop Stop, string Direction, string Towards, string Crowd, string Seats, DateTime ObservedUtc,
        int? SeatCount, int? Standing, string? Comments);

    public static string? NormalizeCrowd(string? text)
    {
        var value = (text ?? "").Trim().ToUpperInvariant();
        return CrowdLevels.All.Contains(value) ? value : null;
    }

    public static string? NormalizeSeats(string? text)
    {
        var value = (text ?? "").Trim().ToUpperInvariant().Replace(' ', '_').Replace('-', '_');
        if (value == "NOSEATS") value = SeatAvailabilities.NoSeats;
        return SeatAvailabilities.All.Contains(value) ? value : null;
    }

    private async Task<ServiceResult<Checked>> CheckAsync(CrowdReportRequest r, DateTime nowUtc)
    {
        var bus = r.BusId is { } busId ? await routes.ByIdAsync(busId) : null;
        if (bus is null)
        {
            return ServiceResult<Checked>.BadRequest("Choose a Dhaka bus and route from the list.");
        }
        var routeId = Guid.Parse(bus.Id);
        var stop = r.BusStopId is { } stopId ? await db.BusStops.AsNoTracking().FirstOrDefaultAsync(s => s.Id == stopId && s.BusId == routeId) : null;
        if (stop is null)
        {
            return ServiceResult<Checked>.BadRequest($"Choose a stop on the {bus.BusName} route.");
        }
        var direction = (r.TravelDirection ?? "").Trim().ToUpperInvariant();
        if (!TravelDirections.All.Contains(direction))
        {
            return ServiceResult<Checked>.BadRequest("Choose the direction the bus was going.");
        }
        if (direction == TravelDirections.Return && !bus.RunsBothWays)
        {
            return ServiceResult<Checked>.BadRequest($"{bus.BusName} only runs one way on this route.");
        }
        var stops = bus.Stops.OrderBy(s => s.Order).ToList();
        var towards = stops.Count == 0 ? "" : direction == TravelDirections.Forward ? stops[^1].Name : stops[0].Name;

        var crowd = NormalizeCrowd(r.CrowdLevel);
        if (crowd is null) return ServiceResult<Checked>.BadRequest("Choose how crowded the bus was: Low, Medium or High.");
        var seats = NormalizeSeats(r.SeatAvailability);
        if (seats is null) return ServiceResult<Checked>.BadRequest("Choose whether seats were Available, Limited or not available.");

        if (!DateOnly.TryParseExact(r.ObservationDate?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            || !TimeOnly.TryParseExact(r.ObservationTime?.Trim(), ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            || DhakaTime.ParseToUtc($"{r.ObservationDate!.Trim()}T{r.ObservationTime!.Trim()}") is not { } observed)
        {
            return ServiceResult<Checked>.BadRequest("Enter the date and time you saw the bus.");
        }
        if (observed > nowUtc.AddMinutes(FutureToleranceMinutes))
        {
            return ServiceResult<Checked>.BadRequest("The observation time cannot be in the future.");
        }
        if (observed < nowUtc.AddHours(-MaxHoursBack))
        {
            return ServiceResult<Checked>.BadRequest($"Only report buses you saw in the last {MaxHoursBack} hours.");
        }

        if (r.AvailableSeatCount is < 0 or > 100) return ServiceResult<Checked>.BadRequest("Free seats must be between 0 and 100.");
        if (r.StandingPassengerCount is < 0 or > 200) return ServiceResult<Checked>.BadRequest("Standing passengers must be between 0 and 200.");
        if (seats == SeatAvailabilities.NoSeats && r.AvailableSeatCount > 0)
        {
            return ServiceResult<Checked>.BadRequest("You chose No Seats, so the number of free seats must be 0 or left empty.");
        }
        if (seats != SeatAvailabilities.NoSeats && r.AvailableSeatCount == 0)
        {
            return ServiceResult<Checked>.BadRequest("You entered 0 free seats. Choose No Seats, or enter how many were free.");
        }

        var comments = string.Join(' ', (r.AdditionalComments ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (comments.Length > MaxComments) return ServiceResult<Checked>.BadRequest($"Comments can be at most {MaxComments} characters.");

        // Saved as whole minutes, like the time the user typed.
        observed = new DateTime(observed.Ticks - observed.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
        return ServiceResult<Checked>.Ok(new Checked(bus, stop, direction, towards, crowd, seats, observed, r.AvailableSeatCount, r.StandingPassengerCount,
            comments.Length == 0 ? null : comments));
    }

    // The same person reporting the same bus at the same stop for (nearly) the same moment.
    private Task<bool> IsDuplicateAsync(Guid userId, Checked c, Guid? exceptId)
    {
        var from = c.ObservedUtc.AddMinutes(-DuplicateWindowMinutes);
        var to = c.ObservedUtc.AddMinutes(DuplicateWindowMinutes);
        var busId = Guid.Parse(c.Bus.Id);
        return db.BusCrowdReports.AnyAsync(r => r.UserId == userId && r.BusId == busId && r.BusStopId == c.Stop.Id
            && r.ObservationDateTime > from && r.ObservationDateTime < to && (exceptId == null || r.Id != exceptId));
    }

    private string DuplicateMessage(Checked c) =>
        $"You already reported {c.Bus.BusName} at {c.Stop.Name} around {DhakaTime.Clock(c.ObservedUtc)}. Edit that report from My Reports instead.";

    private static void Apply(BusCrowdReport report, Checked c, DateTime nowUtc)
    {
        report.BusId = Guid.Parse(c.Bus.Id);
        report.BusStopId = c.Stop.Id;
        report.BusName = Cut(c.Bus.BusName, 100);
        report.RouteNumber = c.Bus.RouteNumber is { } n ? Cut(n, 40) : null;
        report.RouteName = RouteText(c.Bus);
        report.StopName = Cut(c.Stop.Name, 100);
        report.TravelDirection = c.Direction;
        report.TowardsStop = Cut(c.Towards, 100);
        report.CrowdLevel = c.Crowd;
        report.SeatAvailability = c.Seats;
        report.AvailableSeatCount = c.SeatCount;
        report.StandingPassengerCount = c.Standing;
        report.ObservationDateTime = c.ObservedUtc;
        report.ObservationSlot = BusCrowdReport.SlotOf(c.ObservedUtc);
        report.AdditionalComments = c.Comments;
        report.UpdatedAt = nowUtc;
    }

    private static string Cut(string text, int max) => text.Length > max ? text[..max] : text;

    private static string RouteText(BusViewModel bus)
    {
        var stops = bus.Stops.OrderBy(s => s.Order).ToList();
        var ends = stops.Count >= 2 ? $"{stops[0].Name} → {stops[^1].Name}" : "";
        var text = string.IsNullOrWhiteSpace(bus.RouteNumber) ? ends : ends.Length == 0 ? $"Route {bus.RouteNumber}" : $"Route {bus.RouteNumber}: {ends}";
        return Cut(text, 160);
    }

    private static bool IsUniqueViolation(DbUpdateException e) => e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    // ---- Changing ----

    public async Task<ServiceResult<BusCrowdReport>> CreateAsync(Guid userId, CrowdReportRequest request)
    {
        var now = DateTime.UtcNow;
        var check = await CheckAsync(request, now);
        if (!check.Succeeded) return check.As<BusCrowdReport>();
        var c = check.Value!;
        if (await IsDuplicateAsync(userId, c, null))
        {
            return ServiceResult<BusCrowdReport>.Conflict(DuplicateMessage(c));
        }
        var report = new BusCrowdReport { UserId = userId, CreatedAt = now };
        Apply(report, c, now);
        db.BusCrowdReports.Add(report);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException e) when (IsUniqueViolation(e))
        {
            db.ChangeTracker.Clear();
            return ServiceResult<BusCrowdReport>.Conflict(DuplicateMessage(c));
        }
        return ServiceResult<BusCrowdReport>.Created(report, "Thank you! Your crowd report is now live for other commuters.");
    }

    // Only the person who made it, and only in the first EditWindowMinutes after submitting.
    public async Task<ServiceResult<BusCrowdReport>> UpdateAsync(Guid userId, Guid id, CrowdReportRequest request)
    {
        var now = DateTime.UtcNow;
        var report = await db.BusCrowdReports.FirstOrDefaultAsync(r => r.Id == id);
        if (report is null) return ServiceResult<BusCrowdReport>.NotFound("Report not found.");
        if (report.UserId != userId) return ServiceResult<BusCrowdReport>.Forbidden("You can only edit your own reports.");
        if (now >= report.CreatedAt.AddMinutes(EditWindowMinutes))
        {
            return ServiceResult<BusCrowdReport>.Conflict($"Reports can only be edited in the first {EditWindowMinutes} minutes. Submit a new report instead.");
        }
        var check = await CheckAsync(request, now);
        if (!check.Succeeded) return check.As<BusCrowdReport>();
        var c = check.Value!;
        if (await IsDuplicateAsync(userId, c, id))
        {
            return ServiceResult<BusCrowdReport>.Conflict(DuplicateMessage(c));
        }
        Apply(report, c, now);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException e) when (IsUniqueViolation(e))
        {
            db.ChangeTracker.Clear();
            return ServiceResult<BusCrowdReport>.Conflict(DuplicateMessage(c));
        }
        return ServiceResult<BusCrowdReport>.Ok(report, "Your report was updated.");
    }

    public async Task<ServiceResult<bool>> DeleteAsync(Guid userId, Guid id)
    {
        var report = await db.BusCrowdReports.FirstOrDefaultAsync(r => r.Id == id);
        if (report is null) return ServiceResult<bool>.NotFound("Report not found.");
        if (report.UserId != userId) return ServiceResult<bool>.Forbidden("You can only delete your own reports.");
        db.BusCrowdReports.Remove(report);
        await db.SaveChangesAsync();
        return ServiceResult<bool>.Ok(true, "Report deleted.");
    }
}
