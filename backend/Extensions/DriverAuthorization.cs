using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Extensions;

// F21 policies. The role in the token only says "driver"; whether the driver is verified (and assigned to a bus)
// is read from the database on every request, so a decision takes effect at once and a client cannot claim it.
public static class DriverPolicies
{
    // A driver whose application an admin approved.
    public const string Approved = "ApprovedDriver";

    // An approved driver an admin has also assigned to at least one bus: needed for bus operations.
    public const string Assigned = "AssignedDriver";
}

public class DriverAccessRequirement(bool needsBusAssignment) : IAuthorizationRequirement
{
    public bool NeedsBusAssignment { get; } = needsBusAssignment;
}

public class DriverAccessHandler(AppDbContext db, IMemoryCache cache) : AuthorizationHandler<DriverAccessRequirement>
{
    // An approval is remembered for 30 seconds so a driver clicking through the console does not pay a database
    // round trip per request. Only "approved" is cached: a newly approved driver gets access on the next request.
    // (Approval cannot be withdrawn in F21; signing out or a deleted account is still caught by the session check.)
    public static string CacheKey(Guid userId) => $"driver-approved:{userId}";

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, DriverAccessRequirement requirement)
    {
        if (!string.Equals(context.User.FindFirst("role")?.Value, UserRoles.Driver, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        var userId = context.User.GetUserId();
        if (!cache.TryGetValue(CacheKey(userId), out _))
        {
            var approved = await db.DriverProfiles.AnyAsync(p => p.UserId == userId
                && p.VerificationStatus == DriverVerificationStatus.Approved && p.AccountStatus == DriverAccountStatus.Active);
            if (!approved)
            {
                return;
            }
            cache.Set(CacheKey(userId), true, TimeSpan.FromSeconds(30));
        }
        if (requirement.NeedsBusAssignment && !await db.DriverBusAssignments.AnyAsync(a => a.DriverId == userId && a.IsActive))
        {
            return;
        }
        context.Succeed(requirement);
    }
}
