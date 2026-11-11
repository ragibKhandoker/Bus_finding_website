using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace SmartCommute.Server.Services;

// Encrypts sensitive document numbers (driving licence, NID) before they are stored, and makes a keyed hash of each
// for duplicate checks. AES-256-GCM: every value has its own random nonce, and the tag means a changed ciphertext
// is refused rather than decrypted to garbage. Stored as "v1:" + base64(nonce | tag | ciphertext).
//
// The key comes from DriverVerification:EncryptionKey (base64, 32 bytes). Without it a key is derived from
// Jwt:Secret, so a fresh setup works, but then rotating the JWT secret would make stored numbers unreadable.
public sealed class SensitiveDataProtector
{
    private const string Prefix = "v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _encryptionKey;
    private readonly byte[] _hashKey;

    public SensitiveDataProtector(IOptions<DriverVerificationOptions> options, IOptions<JwtOptions> jwt)
    {
        byte[] master;
        var configured = options.Value.EncryptionKey?.Trim();
        if (!string.IsNullOrEmpty(configured))
        {
            try
            {
                master = Convert.FromBase64String(configured);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException("DriverVerification:EncryptionKey must be base64 of 32 random bytes.");
            }
            if (master.Length != 32)
            {
                throw new InvalidOperationException("DriverVerification:EncryptionKey must be base64 of exactly 32 bytes.");
            }
        }
        else
        {
            if (string.IsNullOrEmpty(jwt.Value.Secret))
            {
                throw new InvalidOperationException("Set DriverVerification:EncryptionKey (or Jwt:Secret) to protect driver documents.");
            }
            master = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(jwt.Value.Secret), 32,
                info: "smartcommute-driver-pii-master-v1"u8.ToArray());
        }
        // Separate keys for encryption and for hashing, so neither use weakens the other.
        _encryptionKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, master, 32, info: "encrypt"u8.ToArray());
        _hashKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, master, 32, info: "blind-index"u8.ToArray());
    }

    // Upper-case without spaces and dashes, so "dk-0123 456" and "DK0123456" are the same document.
    public static string Normalize(string value) =>
        new string(value.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();

    public string Encrypt(string plaintext)
    {
        var data = Encoding.UTF8.GetBytes(plaintext);
        var output = new byte[NonceSize + TagSize + data.Length];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_encryptionKey, TagSize);
        aes.Encrypt(nonce, data, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize));
        return Prefix + Convert.ToBase64String(output);
    }

    // Null when the value cannot be decrypted with this key (wrong key, or the value was changed).
    public string? Decrypt(string stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }
        try
        {
            var input = Convert.FromBase64String(stored[Prefix.Length..]);
            if (input.Length < NonceSize + TagSize)
            {
                return null;
            }
            var plain = new byte[input.Length - NonceSize - TagSize];
            using var aes = new AesGcm(_encryptionKey, TagSize);
            aes.Decrypt(input.AsSpan(0, NonceSize), input.AsSpan(NonceSize + TagSize), input.AsSpan(NonceSize, TagSize), plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }

    // A keyed hash of the normalised value ("purpose" keeps a licence and an NID with the same digits apart).
    public string Hash(string purpose, string value) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_hashKey, Encoding.UTF8.GetBytes($"{purpose}:{Normalize(value)}")));

    // "••••••1234": never more than the last four characters.
    public static string Mask(string last4) => string.IsNullOrEmpty(last4) ? "••••••••" : $"••••••{last4}";

    public static string Last4(string value)
    {
        var normalized = Normalize(value);
        return normalized.Length <= 4 ? "" : normalized[^4..];
    }
}
