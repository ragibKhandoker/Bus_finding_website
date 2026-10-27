using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// Account verification: a user uploads an NID or student pass image and submits it; an admin approves or rejects.
// Document images are private: only their owner and admins can fetch them, through this controller.
[Authorize]
[Route("verify")]
public class VerifyController(AppDbContext db, ImageStorageService images, NotificationService notifications) : ApiControllerBase
{
    // Body: { "image": "data:image/jpeg;base64,..." }. Returns the image id to send with submit.
    [HttpPost("upload-image")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(UploadImageRequest request)
    {
        try
        {
            var image = await images.SaveAsync(CurrentUserId, request.Image);
            return Created201(new
            {
                success = true,
                message = "Image uploaded.",
                imageId = image.Id.ToString(),
                // Not a public link: fetching it needs the owner's or an admin's token.
                imageUrl = $"/verify/images/{image.Id}",
                contentType = image.ContentType,
                size = image.SizeBytes,
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequestError(ex.Message);
        }
    }

    [HttpPost("submit")]
    public async Task<IActionResult> Submit(SubmitVerificationRequest request)
    {
        var type = request.DocumentType?.Trim().ToLowerInvariant() ?? VerificationDocumentTypes.StudentPass;
        if (!VerificationDocumentTypes.All.Contains(type))
        {
            return BadRequestError("Choose NID or student pass.");
        }
        var student = type == VerificationDocumentTypes.StudentPass;
        if (student && (string.IsNullOrWhiteSpace(request.InstitutionName) || string.IsNullOrWhiteSpace(request.InstitutionId)))
        {
            return BadRequestError("Enter your institution's name and your student ID.");
        }
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user is null)
        {
            return Error(StatusCodes.Status401Unauthorized, "Please sign in to continue.");
        }
        var image = request.ImageId is { } imageId
            ? await db.VerificationImages.FirstOrDefaultAsync(i => i.Id == imageId && i.UserId == user.Id)
            : null;
        if (image is null)
        {
            return BadRequestError("Upload a photo of your document first.");
        }
        if (await db.Verifications.AnyAsync(v => v.ImageId == image.Id))
        {
            return BadRequestError("That photo was already used for a request. Upload it again.");
        }
        if (await db.Verifications.AnyAsync(v => v.UserId == user.Id && v.Status == VerificationStatus.Hold))
        {
            return Error(StatusCodes.Status409Conflict, "Your previous request is still waiting for review.");
        }
        if (await db.Verifications.AnyAsync(v => v.UserId == user.Id && v.DocumentType == type && v.Status == VerificationStatus.Approved))
        {
            return Error(StatusCodes.Status409Conflict, "You are already verified.");
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
            return Error(StatusCodes.Status409Conflict, "Your previous request is still waiting for review.");
        }
        return Created201(new { success = true, message = "Verification submitted for review.", data = VerificationViewModel.From(verification) });
    }

    // The latest request (data), plus where the user stands for each document type.
    [HttpGet("my-status")]
    public async Task<IActionResult> MyStatus()
    {
        var mine = await db.Verifications.Where(v => v.UserId == CurrentUserId).OrderByDescending(v => v.CreatedAt).AsNoTracking().ToListAsync();
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == CurrentUserId);
        string? StateOf(string type) => mine.FirstOrDefault(v => v.DocumentType == type) is { } v ? VerificationStatus.Label(v.Status) : null;
        return Ok(new
        {
            success = true,
            data = mine.FirstOrDefault() is { } latest ? VerificationViewModel.From(latest) : null,
            studentPass = new { status = StateOf(VerificationDocumentTypes.StudentPass), verified = user.Pass },
            nid = new { status = StateOf(VerificationDocumentTypes.Nid), verified = user.IdentityVerified },
            history = mine.Take(10).Select(VerificationViewModel.From),
        });
    }

    // The document photo, for its owner or an admin. Images are never cached by shared caches.
    [HttpGet("images/{imageId:guid}")]
    public async Task<IActionResult> Image(Guid imageId)
    {
        var image = await db.VerificationImages.AsNoTracking().FirstOrDefaultAsync(i => i.Id == imageId);
        if (image is null || (image.UserId != CurrentUserId && !IsAdmin))
        {
            return NotFoundError("Image not found.");
        }
        return Stream(images.Open(image));
    }

    // The photo of a request, for its owner or an admin; also covers requests from before images were private.
    [HttpGet("{verificationId:guid}/image")]
    public async Task<IActionResult> RequestImage(Guid verificationId)
    {
        var verification = await db.Verifications.Include(v => v.Image).AsNoTracking().FirstOrDefaultAsync(v => v.Id == verificationId);
        if (verification is null || (verification.UserId != CurrentUserId && !IsAdmin))
        {
            return NotFoundError("Image not found.");
        }
        return Stream(verification.Image is { } image ? images.Open(image) : images.OpenLegacy(verification.ImageUrl));
    }

    private IActionResult Stream(StoredImage? stored)
    {
        if (stored is null)
        {
            return NotFoundError("Image not found.");
        }
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(stored.Content, stored.ContentType);
    }

    // Admin: requests newest first, ?status=pending|approved|rejected&page=1&pageSize=20.
    [Authorize(Roles = UserRoles.Admin)]
    [HttpGet("all")]
    public async Task<IActionResult> All([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
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
            return BadRequestError("Status must be pending, approved or rejected.");
        }
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Verifications.Where(v => stored == null || v.Status == stored);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(v => v.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).AsNoTracking().ToListAsync();
        var pending = await db.Verifications.CountAsync(v => v.Status == VerificationStatus.Hold);
        return Ok(new
        {
            success = true,
            count = items.Count,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling(total / (double)pageSize),
            pending,
            data = items.Select(VerificationViewModel.From),
        });
    }

    [Authorize(Roles = UserRoles.Admin)]
    [HttpPost("approve")]
    public Task<IActionResult> Approve(VerificationIdRequest request) => Review(request.VerificationId!.Value, approve: true, null);

    [Authorize(Roles = UserRoles.Admin)]
    [HttpPost("reject")]
    public Task<IActionResult> Reject(RejectVerificationRequest request)
    {
        var reason = request.Reason?.Trim() ?? "";
        return reason.Length < 3
            ? Task.FromResult<IActionResult>(BadRequestError("Give a reason so the user knows what to fix."))
            : Review(request.VerificationId!.Value, approve: false, reason);
    }

    // The status only changes while the request is pending, so repeating a review (or two admins at once) changes
    // nothing and sends no second notification.
    private async Task<IActionResult> Review(Guid verificationId, bool approve, string? reason)
    {
        var verification = await db.Verifications.AsNoTracking().FirstOrDefaultAsync(v => v.Id == verificationId);
        if (verification is null)
        {
            return NotFoundError("Verification request not found.");
        }
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var now = DateTime.UtcNow;
            Guid? reviewer = CurrentUserId;
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
                return Error(StatusCodes.Status409Conflict, "This request was already reviewed.");
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
                NotificationCategories.Account, CurrentUserId, verification.Id);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            var saved = await db.Verifications.AsNoTracking().FirstAsync(v => v.Id == verificationId);
            return Ok(new
            {
                success = true,
                message = approve ? "Verification approved." : "Verification rejected.",
                data = VerificationViewModel.From(saved),
            });
        });
    }
}
