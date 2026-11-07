using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// Account verification: a user uploads an NID or student pass image and submits it; an admin approves or rejects.
// Document images are private: only their owner and admins can fetch them. The rules live in VerificationService.
[Authorize]
[Route("verify")]
public class VerifyController(VerificationService verifications, ImageStorageService images) : ApiControllerBase
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
        var result = await verifications.SubmitAsync(CurrentUserId, request);
        return result.Succeeded
            ? Created201(new { success = true, message = result.Message, data = VerificationViewModel.From(result.Value!) })
            : Error(result.StatusCode, result.Message);
    }

    // The latest request (data), plus where the user stands for each document type.
    [HttpGet("my-status")]
    public async Task<IActionResult> MyStatus()
    {
        var (mine, user) = await verifications.StatusOfAsync(CurrentUserId);
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
    public async Task<IActionResult> Image(Guid imageId) =>
        Stream(await verifications.OpenImageAsync(imageId, CurrentUserId, IsAdmin));

    // The photo of a request, for its owner or an admin; also covers requests from before images were private.
    [HttpGet("{verificationId:guid}/image")]
    public async Task<IActionResult> RequestImage(Guid verificationId) =>
        Stream(await verifications.OpenRequestImageAsync(verificationId, CurrentUserId, IsAdmin));

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
        var result = await verifications.ListAsync(status, page, pageSize);
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        var list = result.Value!;
        return Ok(new
        {
            success = true,
            count = list.Items.Count,
            total = list.Total,
            page = list.Page,
            pageSize = list.PageSize,
            totalPages = (int)Math.Ceiling(list.Total / (double)list.PageSize),
            pending = list.Pending,
            data = list.Items.Select(VerificationViewModel.From),
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

    private async Task<IActionResult> Review(Guid verificationId, bool approve, string? reason)
    {
        var result = await verifications.ReviewAsync(verificationId, CurrentUserId, approve, reason);
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, data = VerificationViewModel.From(result.Value!) })
            : Error(result.StatusCode, result.Message);
    }
}
