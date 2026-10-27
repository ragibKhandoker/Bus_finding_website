using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

public record StoredImage(Stream Content, string ContentType);

// Stores verification document images (NID, student pass) in a private folder (Verification:StoragePath, outside
// wwwroot), so they are never served as public files; VerifyController streams them only to the owner and admins.
// A file is accepted only when its declared type is JPEG, PNG or WebP, its first bytes really are that format, and
// it is within Verification:MaxImageBytes. File names are random and never come from the user.
//
// On hosts with an ephemeral disk (such as Render's free tier) the folder is wiped on redeploy; point StoragePath at
// a persistent disk, or replace this class with cloud storage that keeps objects private.
public partial class ImageStorageService(IWebHostEnvironment env, IOptions<VerificationOptions> options, AppDbContext db)
{
    private static readonly Dictionary<string, string> Extensions = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
    };

    public int MaxBytes => options.Value.MaxImageBytes;

    private string Folder => Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.StoragePath));

    // Images uploaded before documents were made private, kept where they were.
    private string LegacyFolder => Path.Combine(env.ContentRootPath, "wwwroot", "uploads");

    [GeneratedRegex(@"^[0-9a-f]{32}\.(jpg|png|webp)$")]
    private static partial Regex StorageKeyFormat();

    // The format the bytes actually are, from their signature, or null.
    public static string? Sniff(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }
        return null;
    }

    // Checks and stores an image sent as a data URL ("data:image/png;base64,..."). The record is added to the
    // context and saved here. Throws ArgumentException with a message for the user when the image is refused.
    public async Task<VerificationImage> SaveAsync(Guid userId, string dataUrl)
    {
        if (string.IsNullOrWhiteSpace(dataUrl) || !dataUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Choose a JPEG, PNG or WebP image.");
        }
        var comma = dataUrl.IndexOf(',');
        if (comma < 0 || !dataUrl[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("That image could not be read.");
        }
        var declared = dataUrl[5..comma].Split(';')[0].Trim().ToLowerInvariant();
        if (declared == "image/jpg")
        {
            declared = "image/jpeg";
        }
        if (!Extensions.TryGetValue(declared, out var extension))
        {
            throw new ArgumentException("Only JPEG, PNG or WebP images are allowed.");
        }

        // Base64 is 4 characters per 3 bytes; refuse an oversized upload before decoding it.
        var payload = dataUrl.AsSpan(comma + 1).Trim();
        if ((long)payload.Length * 3 / 4 > MaxBytes + 3)
        {
            throw new ArgumentException($"The image must be at most {MaxBytes / (1024 * 1024)} MB.");
        }
        var bytes = new byte[payload.Length * 3 / 4 + 3];
        if (!Convert.TryFromBase64Chars(payload, bytes, out var length))
        {
            throw new ArgumentException("That image could not be read.");
        }
        if (length == 0)
        {
            throw new ArgumentException("The image is empty.");
        }
        if (length > MaxBytes)
        {
            throw new ArgumentException($"The image must be at most {MaxBytes / (1024 * 1024)} MB.");
        }
        if (Sniff(bytes.AsSpan(0, length)) != declared)
        {
            throw new ArgumentException("That file is not a real JPEG, PNG or WebP image.");
        }

        Directory.CreateDirectory(Folder);
        var key = $"{Guid.NewGuid():N}{extension}";
        await using (var file = new FileStream(Path.Combine(Folder, key), FileMode.CreateNew, FileAccess.Write))
        {
            await file.WriteAsync(bytes.AsMemory(0, length));
        }
        var image = new VerificationImage { UserId = userId, StorageKey = key, ContentType = declared, SizeBytes = length };
        db.VerificationImages.Add(image);
        try
        {
            await db.SaveChangesAsync();
        }
        catch
        {
            File.Delete(Path.Combine(Folder, key));
            throw;
        }
        return image;
    }

    public StoredImage? Open(VerificationImage image) => OpenFile(Folder, image.StorageKey, image.ContentType);

    // An image from before documents were private: ImageUrl ends in "/uploads/<random id>.<ext>".
    public StoredImage? OpenLegacy(string? imageUrl)
    {
        var name = imageUrl?.Split('/', '\\').LastOrDefault()?.ToLowerInvariant();
        if (name is null || !StorageKeyFormat().IsMatch(name))
        {
            return null;
        }
        var type = Path.GetExtension(name) switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
        return OpenFile(LegacyFolder, name, type);
    }

    private static StoredImage? OpenFile(string folder, string key, string contentType)
    {
        if (!StorageKeyFormat().IsMatch(key))
        {
            return null;
        }
        var path = Path.Combine(folder, key);
        return File.Exists(path) ? new StoredImage(File.OpenRead(path), contentType) : null;
    }
}
