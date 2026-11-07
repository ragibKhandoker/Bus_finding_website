using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Services;

// A user's requests (newest first) and their account, for "where do I stand".
public record VerificationStatusOf(List<Verification> Requests, User User);

public record VerificationPage(List<Verification> Items, int Total, int Page, int PageSize, int Pending);

// Account verification: a user submits an uploaded NID or student pass image; an admin approves or rejects it.
// Document images are private: only their owner and admins can open them.
public class VerificationService(AppDbContext db, ImageStorageService images, NotificationService notifications)
{
    public async Task<ServiceResult<Verification>> SubmitAsync(Guid userId, SubmitVerificationRequest request)
    {
        var type = request.DocumentType?.Trim().ToLowerInvariant() ?? VerificationDocumentTypes.StudentPass;
        if (!VerificationDocumentTypes.All.Contains(type))
        {
            return ServiceResult<Verification>.BadRequest("Choose NID or student pass.");
        }
        var student = type == VerificationDocumentTypes.StudentPass;
        if (student && (string.IsNullOrWhiteSpace(request.InstitutionName) || string.IsNullOrWhiteSpace(request.InstitutionId)))
        {
            return ServiceResult<Verification>.BadRequest("Enter your institution's name and your student ID.");
        }
        var user = await db.Users.FindAsync(userId);
        if (user is null)
        {
            return ServiceResult<Verification>.Unauthorized("Please sign in to continue.");
        }
        var image = request.ImageId is { } imageId
            ? await db.VerificationImages.FirstOrDefaultAsync(i => i.Id == imageId && i.UserId == user.Id)
            : null;
        if (image is null)
        {
            return ServiceResult<Verification>.BadRequest("Upload a photo of your document first.");
        }
        if (await db.Verifications.AnyAsync(v => v.ImageId == image.Id))
        {
            return ServiceResult<Verification>.BadRequest("That photo was already used for a request. Upload it again.");
        }
        if (await db.Verifications.AnyAsync(v => v.UserId == user.Id && v.Status == VerificationStatus.Hold))
        {
            return ServiceResult<Verification>.Conflict("Your previous request is still waiting for review.");
        }
        if (await db.Verifications.AnyAsync(v => v.UserId == user.Id && v.DocumentType == type && v.Status == VerificationStatus.Approved))
        {
            return ServiceResult<Verification>.Conflict("You are already verified.");
        }

        var verification = new Verification
        {
            UserId = user.Id,
            UserName = user.Name,
            UserEmail = user.Email,
            DocumentType = type,
            ImageId = image.Id,
            InstitutionName = student ? request.InstitutionName!.Trim() : "",
            InstitutionId = student ? request.InstitutionId!.Trim() : "",
            Gmail = request.Gmail?.Trim() ?? "",
        };
        db.Verifications.Add(verification);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A second submit at the same moment; the database kept one pending request.
            return ServiceResult<Verification>.Conflict("Your previous request is still waiting for review.");
        }
        return ServiceResult<Verification>.Created(verification, "Verification submitted for review.");
    }

    public async Task<VerificationStatusOf> StatusOfAsync(Guid userId)
    {
        var mine = await db.Verifications.Where(v => v.UserId == userId).OrderByDescending(v => v.CreatedAt).AsNoTracking().ToListAsync();
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        return new VerificationStatusOf(mine, user);
    }

    // The document photo, for its owner or an admin; null for anyone else.
    public async Task<StoredImage?> OpenImageAsync(Guid imageId, Guid viewerId, bool viewerIsAdmin)
    {
        var image = await db.VerificationImages.AsNoTracking().FirstOrDefaultAsync(i => i.Id == imageId);
        return image is null || (image.UserId != viewerId && !viewerIsAdmin) ? null : images.Open(image);
    }

    // The photo of a request, for its owner or an admin; also covers requests from before images were private.
    public async Task<StoredImage?> OpenRequestImageAsync(Guid verificationId, Guid viewerId, bool viewerIsAdmin)
    {
        var verification = await db.Verifications.Include(v => v.Image).AsNoTracking().FirstOrDefaultAsync(v => v.Id == verificationId);
        if (verification is null || (verification.UserId != viewerId && !viewerIsAdmin))
        {
            return null;
        }
        return verification.Image is { } image ? images.Open(image) : images.OpenLegacy(verification.ImageUrl);
    }

    // Requests newest first. status: pending, approved, rejected, or empty/all.
    public async Task<ServiceResult<VerificationPage>> ListAsync(string? status, int page, int pageSize)
    {
        var stored = status?.Trim().ToLowerInvariant() switch
        {
            null or "" or "all" => null,
            "pending" or VerificationStatus.Hold => VerificationStatus.Hold,
            VerificationStatus.Approved => VerificationStatus.Approved,
            VerificationStatus.Rejected => VerificationStatus.Rejected,
            _ => "invalid",
        };
        if (stored == "invalid")
        {
            return ServiceResult<VerificationPage>.BadRequest("Status must be pending, approved or rejected.");
        }
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Verifications.Where(v => stored == null || v.Status == stored);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(v => v.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).AsNoTracking().ToListAsync();
        var pending = await db.Verifications.CountAsync(v => v.Status == VerificationStatus.Hold);
        return ServiceResult<VerificationPage>.Ok(new VerificationPage(items, total, page, pageSize, pending));
    }

    // Approves or rejects a request. The status only changes while the request is pending, so repeating a review
    // (or two admins at once) changes nothing and sends no second notification.
    public async Task<ServiceResult<Verification>> ReviewAsync(Guid verificationId, Guid reviewerId, bool approve, string? reason)
    {
        var verification = await db.Verifications.AsNoTracking().FirstOrDefaultAsync(v => v.Id == verificationId);
        if (verification is null)
        {
            return ServiceResult<Verification>.NotFound("Verification request not found.");
        }
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var now = DateTime.UtcNow;
            Guid? reviewer = reviewerId;
            var status = approve ? VerificationStatus.Approved : VerificationStatus.Rejected;
            var changed = await db.Verifications
                .Where(v => v.Id == verificationId && v.Status == VerificationStatus.Hold)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(v => v.Status, status)
                    .SetProperty(v => v.ReviewedAt, now)
                    .SetProperty(v => v.ReviewedById, reviewer)
                    .SetProperty(v => v.RejectionReason, reason));
            if (changed == 0)
            {
                return ServiceResult<Verification>.Conflict("This request was already reviewed.");
            }
            var student = verification.DocumentType == VerificationDocumentTypes.StudentPass;
            if (approve)
            {
                await (student
                    ? db.Users.Where(u => u.Id == verification.UserId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Pass, true))
                    : db.Users.Where(u => u.Id == verification.UserId).ExecuteUpdateAsync(s => s.SetProperty(u => u.IdentityVerified, true)));
            }
            var what = student ? "Student verification" : "NID verification";
            notifications.Notify(verification.UserId,
                approve ? $"{what} approved" : $"{what} rejected",
                approve
                    ? (student ? "Your student status has been verified." : "Your identity has been verified.")
                    : $"Your verification was rejected: {reason} You can submit a new request.",
                approve ? NotificationTypes.Success : NotificationTypes.Warning, "/verification",
                NotificationCategories.Account, reviewerId, verification.Id);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            var saved = await db.Verifications.AsNoTracking().FirstAsync(v => v.Id == verificationId);
            return ServiceResult<Verification>.Ok(saved, approve ? "Verification approved." : "Verification rejected.");
        });
    }
}
