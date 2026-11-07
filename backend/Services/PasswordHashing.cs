using System.Text;
using Microsoft.AspNetCore.Identity;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

public enum PasswordCheck
{
    Failed,
    Ok,
    // Correct, but stored in an older format: save a new bcrypt hash now that the password is known.
    OkNeedsRehash,
}

// Every account password is stored as a bcrypt hash ("$2a$…" / "$2b$…"), never as the password itself.
// Accounts created before bcrypt have an ASP.NET Identity (PBKDF2) hash; those still verify, and are replaced
// with bcrypt the next time the user signs in, since only then is the password known.
public static class PasswordHashing
{
    // bcrypt cost: 2^11 rounds, around a tenth of a second per hash on a normal server.
    public const int WorkFactor = 11;

    // bcrypt only uses the first 72 bytes of a password, so longer ones are refused rather than silently cut.
    public const int MaxBytes = 72;

    private static readonly PasswordHasher<User> Legacy = new();

    public static bool IsTooLong(string password) => Encoding.UTF8.GetByteCount(password) > MaxBytes;

    public static bool IsBcrypt(string? hash) => hash is not null && hash.Length == 60 && hash.StartsWith("$2");

    public static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public static PasswordCheck Verify(User user, string password)
    {
        if (string.IsNullOrEmpty(user.PasswordHash) || string.IsNullOrEmpty(password))
        {
            return PasswordCheck.Failed;
        }
        if (IsBcrypt(user.PasswordHash))
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(password, user.PasswordHash) ? PasswordCheck.Ok : PasswordCheck.Failed;
            }
            catch (BCrypt.Net.SaltParseException)
            {
                return PasswordCheck.Failed;
            }
        }
        try
        {
            return Legacy.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed
                ? PasswordCheck.Failed
                : PasswordCheck.OkNeedsRehash;
        }
        catch (FormatException)
        {
            // Not a hash this server understands.
            return PasswordCheck.Failed;
        }
    }
}
