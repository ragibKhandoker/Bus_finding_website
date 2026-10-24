using System.ComponentModel.DataAnnotations;

namespace SmartCommute.Server.Models;

// One row per sign-in. The session id is the token's "jti" claim, so a token stops working as soon as
// its session is revoked (sign-out) or expires, even though the JWT itself is still signed.
public class UserSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    [MaxLength(300)]
    public string? UserAgent { get; set; }

    public bool IsActive(DateTime now) => RevokedAt is null && ExpiresAt > now;
}
