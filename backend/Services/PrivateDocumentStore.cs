using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace SmartCommute.Server.Services;

// Private storage for files nobody may fetch by URL (driver licence and NID images). Callers keep the returned key
// in the database and stream the file back through an authorised endpoint. The interface is small so a cloud
// object store (with private buckets) can replace the local folder later without touching the services.
public interface IPrivateDocumentStore
{
    // Stores the bytes under a new random key and returns it. The original file name is never used.
    Task<string> SaveAsync(ReadOnlyMemory<byte> content, string extension, CancellationToken ct = default);

    StoredImage? Open(string key, string contentType);

    void Delete(string key);
}

// Files in DriverVerification:StoragePath (default App_Data/driver-documents), outside wwwroot so the static file
// handler can never serve them. Keys are "<32 hex>.<jpg|png|webp>"; anything else is refused, so a key read back
// from the database can never point outside the folder.
public partial class LocalPrivateDocumentStore(IWebHostEnvironment env, IOptions<DriverVerificationOptions> options) : IPrivateDocumentStore
{
    [GeneratedRegex(@"^[0-9a-f]{32}\.(jpg|png|webp)$")]
    private static partial Regex KeyFormat();

    private string Folder => Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.StoragePath));

    public async Task<string> SaveAsync(ReadOnlyMemory<byte> content, string extension, CancellationToken ct = default)
    {
        var key = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        if (!KeyFormat().IsMatch(key))
        {
            throw new ArgumentException("Unsupported file type.");
        }
        Directory.CreateDirectory(Folder);
        await using var file = new FileStream(Path.Combine(Folder, key), FileMode.CreateNew, FileAccess.Write);
        await file.WriteAsync(content, ct);
        return key;
    }

    public StoredImage? Open(string key, string contentType) => ImageStorageService.OpenFile(Folder, key, contentType);

    public void Delete(string key)
    {
        if (!KeyFormat().IsMatch(key))
        {
            return;
        }
        try
        {
            File.Delete(Path.Combine(Folder, key));
        }
        catch (IOException)
        {
            // A file that cannot be removed now is only wasted space; never fail the request over it.
        }
    }
}
