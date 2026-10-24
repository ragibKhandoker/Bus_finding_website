using System.Security.Claims;

namespace SmartCommute.Server.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("id"), out var id) ? id : Guid.Empty;

    public static bool IsAdmin(this ClaimsPrincipal user) =>
        string.Equals(user.FindFirstValue("role"), Models.UserRoles.Admin, StringComparison.OrdinalIgnoreCase);
}
