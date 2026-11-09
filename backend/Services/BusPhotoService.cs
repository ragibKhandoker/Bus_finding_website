using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

// What the client needs to show a route's uploaded photo. Url changes whenever the photo is replaced, so browsers
// can cache each version for a long time.
public record BusPhotoInfo(Guid BusId, string Url, string? Credit, DateTime UpdatedAt)
{
    public static BusPhotoInfo From(BusPhoto photo) =>
        new(photo.BusId, $"/bus/{photo.BusId}/photo?v={photo.UpdatedAt.Ticks:x}", photo.Credit, photo.UpdatedAt);
}

// Admin-uploaded photos of bus routes. Files are checked like verification images (ImageStorageService.Decode),
// stored under a random name, and replaced in place when an admin uploads a new one.
public class BusPhotoService(AppDbContext db, IWebHostEnvironment env, IOptions<BusPhotoOptions> options)
{
    public const int MaxCreditLength = 200;

    private string Folder => Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.StoragePath));

    public async Task<List<BusPhotoInfo>> AllAsync(CancellationToken ct = default) =>
        (await db.BusPhotos.AsNoTracking().OrderBy(p => p.BusId).ToListAsync(ct)).Select(BusPhotoInfo.From).ToList();

    public async Task<StoredImage?> OpenAsync(Guid busId, CancellationToken ct = default)
    {
        var photo = await db.BusPhotos.AsNoTracking().FirstOrDefaultAsync(p => p.BusId == busId, ct);
        return photo is null ? null : ImageStorageService.OpenFile(Folder, photo.StorageKey, photo.ContentType);
    }

    public async Task<ServiceResult<BusPhotoInfo>> SaveAsync(Guid busId, string dataUrl, string? credit, Guid adminId)
    {
        if (!await db.Buses.AnyAsync(b => b.Id == busId))
        {
            return ServiceResult<BusPhotoInfo>.NotFound("Bus not found.");
        }
        credit = string.IsNullOrWhiteSpace(credit) ? null : credit.Trim();
        if (credit?.Length > MaxCreditLength)
        {
            return ServiceResult<BusPhotoInfo>.BadRequest($"The photo credit must be at most {MaxCreditLength} characters.");
        }
        DecodedImage image;
        try
        {
            image = ImageStorageService.Decode(dataUrl, options.Value.MaxImageBytes);
        }
        catch (ArgumentException e)
        {
            return ServiceResult<BusPhotoInfo>.BadRequest(e.Message);
        }

        Directory.CreateDirectory(Folder);
        var key = $"{Guid.NewGuid():N}{image.Extension}";
        await using (var file = new FileStream(Path.Combine(Folder, key), FileMode.CreateNew, FileAccess.Write))
        {
            await file.WriteAsync(image.Bytes.AsMemory(0, image.Length));
        }

        var photo = await db.BusPhotos.FirstOrDefaultAsync(p => p.BusId == busId);
        var oldKey = photo?.StorageKey;
        if (photo is null)
        {
            photo = new BusPhoto { BusId = busId };
            db.BusPhotos.Add(photo);
        }
        photo.StorageKey = key;
        photo.ContentType = image.ContentType;
        photo.SizeBytes = image.Length;
        photo.Credit = credit;
        photo.UploadedById = adminId;
        photo.UpdatedAt = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync();
        }
        catch
        {
            DeleteFile(key);
            throw;
        }
        if (oldKey is not null)
        {
            DeleteFile(oldKey);
        }
        return ServiceResult<BusPhotoInfo>.Ok(BusPhotoInfo.From(photo), "Bus photo saved.");
    }

    public async Task<ServiceResult<bool>> DeleteAsync(Guid busId)
    {
        var photo = await db.BusPhotos.FirstOrDefaultAsync(p => p.BusId == busId);
        if (photo is null)
        {
            return ServiceResult<bool>.NotFound("This bus has no uploaded photo.");
        }
        db.BusPhotos.Remove(photo);
        await db.SaveChangesAsync();
        DeleteFile(photo.StorageKey);
        return ServiceResult<bool>.Ok(true, "Bus photo removed.");
    }

    private void DeleteFile(string key)
    {
        try
        {
            File.Delete(Path.Combine(Folder, key));
        }
        catch (IOException)
        {
            // A file that cannot be removed now is only wasted space; the database no longer points at it.
        }
    }
}
