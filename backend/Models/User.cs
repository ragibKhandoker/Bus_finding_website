using System.ComponentModel.DataAnnotations;

namespace SmartCommute.Server.Models;

public static class UserRoles
{
    public const string Commuter = "commuter";
    public const string Driver = "driver";
    public const string Admin = "admin";

    // Roles a person may pick for themselves at sign-up. Admins are seeded from configuration.
    public static readonly string[] SelfAssignable = [Commuter, Driver];
    public static readonly string[] All = [Commuter, Driver, Admin];
}

public static class UserGenders
{
    public const string Unspecified = "Prefer not to say";

    public static readonly string[] All = [Unspecified, "Male", "Female", "Other"];

    // Matches case-insensitively and returns the stored spelling, or null if the value is not allowed.
    public static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? Unspecified
        : All.FirstOrDefault(g => string.Equals(g, value.Trim(), StringComparison.OrdinalIgnoreCase));
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
    public string Gender { get; set; } = UserGenders.Unspecified;

    // Optional details the user fills in from their profile page.
    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(200)]
    public string? Bio { get; set; }

    // Where the user usually starts and ends their commute, e.g. "Uttara" and "Motijheel".
    [MaxLength(100)]
    public string? HomeArea { get; set; }

    [MaxLength(100)]
    public string? WorkArea { get; set; }

    [MaxLength(20)]
    public string Role { get; set; } = UserRoles.Commuter;

    // True once an admin approves the user's student verification.
    public bool Pass { get; set; }

    // True once an admin approves the user's NID.
    public bool IdentityVerified { get; set; }

    // 5-character code (A-Z, 0-9) used to find and add friends.
    [MaxLength(5)]
    public string FriendCode { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }

    public Wallet? Wallet { get; set; }
}
