using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Extensions;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

public record DriverApplicationFilter(string? Search = null, string? Status = null, DateOnly? From = null, DateOnly? To = null,
    bool Oldest = false, int Page = 1, int PageSize = 15);

public record DriverApplicationStats(int Total, int Pending, int UnderReview, int Approved, int Rejected, int ResubmissionRequired,
    int ActiveAssignments);

// F21, the admin's side: the application queue, the documents, the decisions and bus assignments. Every decision
// is checked against DriverVerificationStatus.AdminNext, written to driver_verification_history with the admin,
// the old and new status and the reason, and the driver is notified, all in one save. The profile's row version
// means two admins deciding at once cannot both win (the second gets 409).
public class DriverVerificationService(AppDbContext db, IPrivateDocumentStore store, NotificationService notifications)
{
    public const int MinReasonLength = 5;

    // ---- Reading ----

    public async Task<(List<DriverProfile> Items, int Total)> ListAsync(DriverApplicationFilter f)
    {
        var query = db.DriverProfiles.AsNoTracking().AsQueryable();
        if (f.Status is { Length: > 0 } status)
        {
            query = query.Where(p => p.VerificationStatus == status);
        }
        if (f.From is { } from)
        {
            var start = DhakaTime.ToUtc(from.ToDateTime(TimeOnly.MinValue));
            query = query.Where(p => p.SubmittedAt >= start);
        }
        if (f.To is { } to)
        {
            var end = DhakaTime.ToUtc(to.AddDays(1).ToDateTime(TimeOnly.MinValue));
            query = query.Where(p => p.SubmittedAt < end);
        }
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var like = f.Search.ContainsPattern();
            query = query.Where(p => EF.Functions.ILike(p.User!.Name, like) || EF.Functions.ILike(p.User.Email, like));
        }
        var total = await query.CountAsync();
        var ordered = f.Oldest
            ? query.OrderBy(p => p.SubmittedAt).ThenBy(p => p.Id)
            : query.OrderByDescending(p => p.SubmittedAt).ThenByDescending(p => p.Id);
        var items = await ordered.Include(p => p.User).Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).ToListAsync();
        return (items, total);
    }

    public async Task<DriverApplicationStats> StatsAsync()
    {
        var counts = await db.DriverProfiles.AsNoTracking().GroupBy(p => p.VerificationStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        int Count(string status) => counts.FirstOrDefault(c => c.Status == status)?.Count ?? 0;
        return new DriverApplicationStats(counts.Sum(c => c.Count), Count(DriverVerificationStatus.Pending), Count(DriverVerificationStatus.UnderReview),
            Count(DriverVerificationStatus.Approved), Count(DriverVerificationStatus.Rejected), Count(DriverVerificationStatus.ResubmissionRequired),
            await db.DriverBusAssignments.CountAsync(a => a.IsActive));
    }

    public Task<int> WaitingCountAsync() => db.DriverProfiles.CountAsync(p =>
        p.VerificationStatus == DriverVerificationStatus.Pending || p.VerificationStatus == DriverVerificationStatus.UnderReview);

    public Task<DriverProfile?> DetailsAsync(Guid driverId) => db.DriverProfiles
        .Include(p => p.User).Include(p => p.Documents).Include(p => p.History).ThenInclude(h => h.Admin)
        .AsSplitQuery().AsNoTracking().FirstOrDefaultAsync(p => p.UserId == driverId);

    public Task<List<DriverBusAssignment>> AssignmentsAsync(Guid driverId) => db.DriverBusAssignments.Include(a => a.Bus).Include(a => a.IndividualBus).AsNoTracking()
        .Where(a => a.DriverId == driverId).OrderByDescending(a => a.IsActive).ThenByDescending(a => a.AssignedAt).ToListAsync();

    // The image for an admin. Null when the driver or document does not exist (or the file is missing).
    public async Task<StoredImage?> DocumentAsync(Guid driverId, string documentType)
    {
        var type = documentType.Trim().ToLowerInvariant();
        if (!DriverDocumentTypes.All.Contains(type))
        {
            return null;
        }
        var document = await db.DriverDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.DriverProfile!.UserId == driverId && d.DocumentType == type);
        return document is null ? null : store.Open(document.PrivateStorageKey, document.ContentType);
    }

    // ---- Decisions ----

    public Task<ServiceResult<DriverProfile>> StartReviewAsync(Guid adminId, Guid driverId) =>
        TransitionAsync(adminId, driverId, DriverVerificationStatus.UnderReview, "Review started.", null);

    public Task<ServiceResult<DriverProfile>> ApproveAsync(Guid adminId, Guid driverId, string? note) =>
        TransitionAsync(adminId, driverId, DriverVerificationStatus.Approved, string.IsNullOrWhiteSpace(note) ? "Application approved." : note.Trim(), null);

    public Task<ServiceResult<DriverProfile>> RejectAsync(Guid adminId, Guid driverId, string? reason)
    {
        var text = reason?.Trim() ?? "";
        return text.Length < MinReasonLength
            ? Task.FromResult(ServiceResult<DriverProfile>.BadRequest("Give a reason for the rejection (at least 5 characters)."))
            : TransitionAsync(adminId, driverId, DriverVerificationStatus.Rejected, text, null);
    }

    public Task<ServiceResult<DriverProfile>> RequestResubmissionAsync(Guid adminId, Guid driverId, string? reason, IEnumerable<string>? fields)
    {
        var text = reason?.Trim() ?? "";
        if (text.Length < MinReasonLength)
        {
            return Task.FromResult(ServiceResult<DriverProfile>.BadRequest("Explain what needs to be corrected (at least 5 characters)."));
        }
        var chosen = (fields ?? []).Select(f => f.Trim().ToLowerInvariant()).Distinct().ToList();
        if (chosen.Count == 0)
        {
            return Task.FromResult(ServiceResult<DriverProfile>.BadRequest("Choose at least one item the driver must correct."));
        }
        if (chosen.Any(f => !DriverResubmissionFields.All.Contains(f)))
        {
            return Task.FromResult(ServiceResult<DriverProfile>.BadRequest("Unknown item to correct."));
        }
        return TransitionAsync(adminId, driverId, DriverVerificationStatus.ResubmissionRequired, text, chosen);
    }

    private async Task<ServiceResult<DriverProfile>> TransitionAsync(Guid adminId, Guid driverId, string to, string reason, List<string>? fields)
    {
        var profile = await db.DriverProfiles.Include(p => p.User).FirstOrDefaultAsync(p => p.UserId == driverId);
        if (profile is null)
        {
            return ServiceResult<DriverProfile>.NotFound("Driver application not found.");
        }
        // Defence in depth: the endpoints are admin-only, but nobody may ever decide on their own application.
        if (adminId == profile.UserId)
        {
            return ServiceResult<DriverProfile>.Forbidden("You cannot review your own application.");
        }
        var from = profile.VerificationStatus;
        if (!DriverVerificationStatus.AdminCanMove(from, to))
        {
            return ServiceResult<DriverProfile>.Conflict($"This application is {Label(from)} and cannot be moved to {Label(to)}.");
        }

        var now = DateTime.UtcNow;
        profile.VerificationStatus = to;
        profile.AccountStatus = DriverVerificationStatus.AccountStatusFor(to);
        profile.ReviewedAt = now;
        profile.ReviewedById = adminId;
        profile.UpdatedAt = now;
        if (to == DriverVerificationStatus.Rejected)
        {
            profile.RejectionReason = reason;
        }
        if (to == DriverVerificationStatus.ResubmissionRequired)
        {
            profile.ResubmissionReason = reason;
            profile.ResubmissionFields = string.Join(',', fields!);
        }
        if (to == DriverVerificationStatus.Approved)
        {
            profile.RejectionReason = null;
            profile.ResubmissionFields = null;
        }
        db.DriverVerificationHistory.Add(new DriverVerificationHistory
        {
            DriverProfileId = profile.Id, AdminId = adminId, PreviousStatus = from, NewStatus = to, Reason = reason, CreatedAt = now,
        });

        var (title, message, type) = to switch
        {
            DriverVerificationStatus.UnderReview => ("Driver application under review",
                "An administrator is now reviewing your driver documents.", NotificationTypes.Info),
            DriverVerificationStatus.Approved => ("Driver account verified",
                "Congratulations! Your driver account has been verified. An administrator will assign you to a bus.", NotificationTypes.Success),
            DriverVerificationStatus.Rejected => ("Driver application not approved",
                $"Unfortunately, your application was not approved. Reason: {reason}", NotificationTypes.Warning),
            _ => ("Corrections needed on your driver application",
                $"Some information or documents need correction: {reason}", NotificationTypes.Warning),
        };
        notifications.Notify(profile.UserId, title, message, type, "/driver/status", NotificationCategories.Account, adminId, profile.Id);
        await db.SaveChangesAsync();

        var saved = (await DetailsAsync(driverId))!;
        return ServiceResult<DriverProfile>.Ok(saved, to switch
        {
            DriverVerificationStatus.UnderReview => "The application is now under review.",
            DriverVerificationStatus.Approved => "Driver approved. They were notified.",
            DriverVerificationStatus.Rejected => "Application rejected. The driver was notified.",
            _ => "Resubmission requested. The driver was notified.",
        });
    }

    // ---- Bus assignments ----

    // Only an admin assigns a bus, and only to an approved driver.
    public async Task<ServiceResult<DriverBusAssignment>> AssignBusAsync(Guid adminId, Guid driverId, Guid busId, Guid? individualBusId = null)
    {
        var profile = await db.DriverProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == driverId);
        if (profile is null)
        {
            return ServiceResult<DriverBusAssignment>.NotFound("Driver application not found.");
        }
        if (profile.VerificationStatus != DriverVerificationStatus.Approved || profile.AccountStatus != DriverAccountStatus.Active)
        {
            return ServiceResult<DriverBusAssignment>.Conflict("Approve the driver before assigning a bus.");
        }
        var bus = await db.Buses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == busId);
        if (bus is null)
        {
            return ServiceResult<DriverBusAssignment>.NotFound("Bus not found.");
        }
        if (await db.DriverBusAssignments.AnyAsync(a => a.DriverId == driverId && a.BusId == busId && a.IsActive))
        {
            return ServiceResult<DriverBusAssignment>.Conflict("This driver is already assigned to that bus.");
        }
        IndividualBus? vehicle = null;
        if (individualBusId is { } vid)
        {
            vehicle = await db.IndividualBuses.AsNoTracking().FirstOrDefaultAsync(v => v.Id == vid && v.BusId == busId);
            if (vehicle is null)
            {
                return ServiceResult<DriverBusAssignment>.BadRequest("That vehicle does not run on this route.");
            }
        }
        var assignment = new DriverBusAssignment { DriverId = driverId, BusId = busId, IndividualBusId = vehicle?.Id, AssignedByAdminId = adminId, AssignedAt = DateTime.UtcNow };
        db.DriverBusAssignments.Add(assignment);
        notifications.Notify(driverId, "Bus assigned", $"You have been assigned to {bus.BusName}{(bus.RouteNumber is null ? "" : $" ({bus.RouteNumber})")}.",
            NotificationTypes.Success, "/driver/dashboard", NotificationCategories.Account, adminId, assignment.Id);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return ServiceResult<DriverBusAssignment>.Conflict("This driver is already assigned to that bus.");
        }
        assignment.Bus = bus;
        assignment.IndividualBus = vehicle;
        return ServiceResult<DriverBusAssignment>.Created(assignment, $"Assigned to {bus.BusName}.");
    }

    // F22: the vehicle on the assigned route. Changing it does not change trips already scheduled.
    public async Task<ServiceResult<DriverBusAssignment>> SetAssignmentVehicleAsync(Guid assignmentId, Guid? individualBusId)
    {
        var assignment = await db.DriverBusAssignments.Include(a => a.Bus).FirstOrDefaultAsync(a => a.Id == assignmentId);
        if (assignment is null)
        {
            return ServiceResult<DriverBusAssignment>.NotFound("Assignment not found.");
        }
        if (!assignment.IsActive)
        {
            return ServiceResult<DriverBusAssignment>.Conflict("This assignment has ended.");
        }
        IndividualBus? vehicle = null;
        if (individualBusId is { } vid)
        {
            vehicle = await db.IndividualBuses.FirstOrDefaultAsync(v => v.Id == vid && v.BusId == assignment.BusId);
            if (vehicle is null)
            {
                return ServiceResult<DriverBusAssignment>.BadRequest("That vehicle does not run on this route.");
            }
        }
        assignment.IndividualBusId = vehicle?.Id;
        assignment.IndividualBus = vehicle;
        await db.SaveChangesAsync();
        return ServiceResult<DriverBusAssignment>.Ok(assignment, vehicle is null ? "Vehicle removed from the assignment." : $"Vehicle {vehicle.BusCode} assigned.");
    }

    public Task<Guid?> AssignmentDriverAsync(Guid assignmentId) =>
        db.DriverBusAssignments.Where(a => a.Id == assignmentId).Select(a => (Guid?)a.DriverId).FirstOrDefaultAsync();

    public async Task<ServiceResult<DriverBusAssignment>> EndAssignmentAsync(Guid adminId, Guid driverId, Guid assignmentId)
    {
        var assignment = await db.DriverBusAssignments.Include(a => a.Bus).FirstOrDefaultAsync(a => a.Id == assignmentId && a.DriverId == driverId);
        if (assignment is null)
        {
            return ServiceResult<DriverBusAssignment>.NotFound("Assignment not found.");
        }
        if (!assignment.IsActive)
        {
            return ServiceResult<DriverBusAssignment>.Conflict("This assignment has already ended.");
        }
        assignment.IsActive = false;
        assignment.EndedAt = DateTime.UtcNow;
        assignment.EndedByAdminId = adminId;
        // F22: future trips on that bus can no longer run, so they are cancelled (the driver is told once, below).
        var now = DateTime.UtcNow;
        var future = await db.DriverTrips.Where(t => t.DriverId == driverId && t.BusId == assignment.BusId
            && t.Status == DriverTripStatus.Scheduled && t.ScheduledDeparture > now).ToListAsync();
        foreach (var trip in future)
        {
            trip.Status = DriverTripStatus.Cancelled;
            trip.CancelReason = "The bus assignment ended.";
            trip.UpdatedByAdminId = adminId;
            trip.UpdatedAt = now;
        }
        notifications.Notify(driverId, "Bus assignment ended", $"You are no longer assigned to {assignment.Bus?.BusName ?? "that bus"}."
            + (future.Count > 0 ? $" {future.Count} upcoming trip(s) on it were cancelled." : ""),
            NotificationTypes.Info, "/driver/dashboard", NotificationCategories.Account, adminId, assignment.Id);
        await db.SaveChangesAsync();
        return ServiceResult<DriverBusAssignment>.Ok(assignment, "Assignment ended.");
    }

    public static string Label(string status) => status switch
    {
        DriverVerificationStatus.Pending => "pending",
        DriverVerificationStatus.UnderReview => "under review",
        DriverVerificationStatus.Approved => "approved",
        DriverVerificationStatus.Rejected => "rejected",
        DriverVerificationStatus.ResubmissionRequired => "waiting for resubmission",
        _ => status.ToLowerInvariant(),
    };
}
