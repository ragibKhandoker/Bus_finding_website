namespace SmartCommute.Server.Services;

// Stores verification ID images under wwwroot/uploads. On hosts with an ephemeral disk
// (such as Render's free tier) uploads are lost on redeploy; swap this for cloud storage there.
public class ImageStorageService(IWebHostEnvironment env)
{
    private const int MaxBytes = 5 * 1024 * 1024;

    private static readonly Dictionary<string, string> Extensions = new()
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/jpg"] = ".jpg",
    };

    // Accepts a data URL ("data:image/png;base64,...") or bare base64. Returns the relative URL path.
    public async Task<string> SaveBase64Async(string image)
    {
        var mime = "image/jpeg";
        var payload = image;
        if (image.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = image.IndexOf(',');
            if (comma < 0)
            {
                throw new ArgumentException("Invalid image data.");
            }
            mime = image[5..comma].Split(';')[0].ToLowerInvariant();
            payload = image[(comma + 1)..];
        }

        if (!Extensions.TryGetValue(mime, out var extension))
        {
            throw new ArgumentException("Only PNG or JPEG images are allowed.");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(payload);
        }
        catch (FormatException)
        {
            throw new ArgumentException("Invalid image data.");
        }

        if (bytes.Length == 0 || bytes.Length > MaxBytes)
        {
            throw new ArgumentException("Image must be smaller than 5 MB.");
        }

        var folder = Path.Combine(env.ContentRootPath, "wwwroot", "uploads");
        Directory.CreateDirectory(folder);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        await File.WriteAllBytesAsync(Path.Combine(folder, fileName), bytes);
        return $"/uploads/{fileName}";
    }
}
