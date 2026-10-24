using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Server.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration config, ILogger logger)
    {
        var db = services.GetRequiredService<AppDbContext>();

        if (!await db.Buses.AnyAsync())
        {
            db.Buses.AddRange(SeedData.Buses());
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded {Count} Dhaka bus routes", await db.Buses.CountAsync());
        }

        var adminEmail = config["Admin:Email"]?.Trim().ToLowerInvariant();
        var adminPassword = config["Admin:Password"];
        if (string.IsNullOrEmpty(adminEmail) || string.IsNullOrEmpty(adminPassword))
        {
            return;
        }

        var admin = await db.Users.FirstOrDefaultAsync(u => u.Email == adminEmail);
        if (admin is null)
        {
            var friendCodes = services.GetRequiredService<FriendCodeGenerator>();
            admin = new User
            {
                Name = "Admin",
                Email = adminEmail,
                Role = UserRoles.Admin,
                FriendCode = await friendCodes.GenerateUniqueAsync(),
            };
            admin.PasswordHash = new PasswordHasher<User>().HashPassword(admin, adminPassword);
            db.Users.Add(admin);
            db.Wallets.Add(new Wallet { User = admin });
            await db.SaveChangesAsync();
            logger.LogInformation("Created admin account {Email}", adminEmail);
        }
        else if (admin.Role != UserRoles.Admin)
        {
            admin.Role = UserRoles.Admin;
            await db.SaveChangesAsync();
            logger.LogInformation("Promoted {Email} to admin", adminEmail);
        }
    }
}
