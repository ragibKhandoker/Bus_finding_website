using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;

namespace SmartCommute.Server.Services;

public class FriendCodeGenerator(AppDbContext db)
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public async Task<string> GenerateUniqueAsync()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var code = RandomNumberGenerator.GetString(Alphabet, 5);
            if (!await db.Users.AnyAsync(u => u.FriendCode == code))
            {
                return code;
            }
        }
        throw new InvalidOperationException("Could not generate a unique friend code.");
    }
}
