using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Data;

namespace SmartCommute.Server.Services;

// Friend codes are 5 characters of A-Z and 0-9. The unique index on users.friend_code is what finally
// guarantees uniqueness: the lookup here only makes a clash at insert time rare, and callers that insert a
// user retry with a fresh code when IsCodeClash says the insert lost a race.
public partial class FriendCodeGenerator(AppDbContext db)
{
    public const int Length = 5;
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    [GeneratedRegex("^[A-Z0-9]{5}$")]
    private static partial Regex FormatRegex();

    // A random code from a cryptographically secure generator, without checking the database.
    public static string Generate() => RandomNumberGenerator.GetString(Alphabet, Length);

    // Trims and upper-cases what a user typed; returns null unless the result is a well-formed code.
    public static string? Normalize(string? code)
    {
        var normalized = code?.Trim().ToUpperInvariant() ?? "";
        return FormatRegex().IsMatch(normalized) ? normalized : null;
    }

    // True when a failed save was rejected because another user already has the friend code.
    public static bool IsCodeClash(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && pg.ConstraintName?.Contains("friend_code") == true;

    public async Task<string> GenerateUniqueAsync()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var code = Generate();
            if (!await db.Users.AnyAsync(u => u.FriendCode == code))
            {
                return code;
            }
        }
        throw new InvalidOperationException("Could not generate a unique friend code.");
    }
}
