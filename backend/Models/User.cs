using System.ComponentModel.DataAnnotations;

namespace SmartCommute.Server.Models;

public static class UserRoles
{
    public const string Commuter = "commuter";
    public const string Driver = "driver";
    public const string Admin = "admin";

    // Roles a person may pick for themselves at sign-up. Admins are seeded from configuration.
    public static readonly string[] SelfAssignable = [Commuter, Driver];
}

public class User
{
    public Guid Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = "";

    // Always stored lower-cased so lookups are case-insensitive.
    [MaxLength(255)]
    public string Email { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    [MaxLength(30)]
    public string Gender { get; set; } = "Prefer not to say";

    [MaxLength(20)]
    public string Role { get; set; } = UserRoles.Commuter;

    // True once an admin approves the user's student verification.
    public bool Pass { get; set; }

    // 5-character code (A-Z, 0-9) used to find and add friends.
    [MaxLength(5)]
    public string FriendCode { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Wallet? Wallet { get; set; }
}
