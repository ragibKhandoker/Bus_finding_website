using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// Student pass verification: users submit an institution ID, admins approve or reject it.
[Authorize]
[Route("verify")]
public class VerifyController(AppDbContext db, ImageStorageService images, NotificationService notifications) : ApiControllerBase
{
    [HttpPost("upload-image")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(UploadImageRequest request)
    {
        try
        {
            var path = await images.SaveBase64Async(request.Image);
            var imageUrl = $"{Request.Scheme}://{Request.Host}{path}";
            return Ok(new { success = true, message = "Image uploaded.", imageUrl });
        }
        catch (ArgumentException ex)
        {
            return BadRequestError(ex.Message);
        }
    }

    [HttpPost("submit")]
    public async Task<IActionResult> Submit(SubmitVerificationRequest request)
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user is null)
        {
            return NotFoundError("User not found.");
        }

        var latest = await Latest(user.Id);
        if (latest?.Status == VerificationStatus.Hold)
        {
            return BadRequestError("Your previous request is still waiting for review.");
        }
        if (latest?.Status == VerificationStatus.Approved)
        {
            return BadRequestError("You are already verified.");
        }

        var verification = new Verification
        {
            UserId = user.Id,
            UserName = user.Name,
            UserEmail = user.Email,
            InstitutionName = request.InstitutionName.Trim(),
            InstitutionId = request.InstitutionId.Trim(),
            Gmail = request.Gmail.Trim(),
            ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim(),
        };
        db.Verifications.Add(verification);
        await db.SaveChangesAsync();
        return Created201(new { success = true, message = "Verification submitted for review.", data = VerificationViewModel.From(verification) });
    }

    [HttpGet("my-status")]
    public async Task<IActionResult> MyStatus()
    {
        var latest = await Latest(CurrentUserId);
        return Ok(new { success = true, data = latest is null ? null : VerificationViewModel.From(latest) });
    }

    [Authorize(Roles = UserRoles.Admin)]
    [HttpGet("all")]
    public async Task<IActionResult> All()
    {
        var items = await db.Verifications.OrderByDescending(v => v.CreatedAt).AsNoTracking().ToListAsync();
        return Ok(new { success = true, count = items.Count, data = items.Select(VerificationViewModel.From) });
    }

    [Authorize(Roles = UserRoles.Admin)]
    [HttpPost("approve")]
    public Task<IActionResult> Approve(VerificationIdRequest request) => Review(request.VerificationId!.Value, approve: true);

    [Authorize(Roles = UserRoles.Admin)]
    [HttpPost("reject")]
    public Task<IActionResult> Reject(VerificationIdRequest request) => Review(request.VerificationId!.Value, approve: false);

    private async Task<IActionResult> Review(Guid verificationId, bool approve)
    {
        var verification = await db.Verifications.Include(v => v.User).FirstOrDefaultAsync(v => v.Id == verificationId);
        if (verification is null)
        {
            return NotFoundError("Verification request not found.");
        }
        if (verification.Status != VerificationStatus.Hold)
        {
            return BadRequestError("This request was already reviewed.");
        }

        verification.Status = approve ? VerificationStatus.Approved : VerificationStatus.Rejected;
        verification.ReviewedAt = DateTime.UtcNow;
        if (approve)
        {
            verification.User!.Pass = true;
        }
        notifications.Notify(verification.UserId,
            approve ? "Student verification approved" : "Student verification rejected",
            approve ? "Your student status has been verified." : "Your verification was rejected. You can submit a new request.",
            approve ? NotificationTypes.Success : NotificationTypes.Warning);
        await db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = approve ? "Verification approved." : "Verification rejected.",
            data = VerificationViewModel.From(verification),
        });
    }

    private Task<Verification?> Latest(Guid userId) =>
        db.Verifications.Where(v => v.UserId == userId).OrderByDescending(v => v.CreatedAt).FirstOrDefaultAsync();
}
