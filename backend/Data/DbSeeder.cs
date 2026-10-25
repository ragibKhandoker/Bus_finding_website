using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Server.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration config, ILogger logger)
    {
        var db = services.GetRequiredService<AppDbContext>();
        await SeedBusRoutesAsync(db, logger);

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

    // Adds the starter routes that are not in the database yet, matched by route number. Routes already there,
    // and any other buses, are left untouched, so restarting the server never duplicates a route.
    private static async Task SeedBusRoutesAsync(AppDbContext db, ILogger logger)
    {
        var existing = (await db.Buses.Where(b => b.RouteNumber != null).Select(b => b.RouteNumber!).ToListAsync()).ToHashSet();
        var missing = SeedData.Buses().Where(b => !existing.Contains(b.RouteNumber!)).ToList();
        if (missing.Count == 0)
        {
            logger.LogInformation("Bus routes already seeded ({Count} routes in the database)", existing.Count);
            return;
        }

        db.Buses.AddRange(missing);
        try
        {
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded {Count} Dhaka bus routes", missing.Count);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another server instance added the same routes first; the unique route number kept them single.
            db.ChangeTracker.Clear();
            logger.LogInformation("Bus routes were seeded by another instance");
        }
    }
}
