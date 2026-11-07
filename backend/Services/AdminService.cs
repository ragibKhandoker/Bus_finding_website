using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

// Counts for the admin dashboard and profile.
public record AdminOverview(
    int TotalUsers, int Commuters, int Drivers, int Admins, int NewThisWeek,
    int PendingPasswordRequests, int UnreadMessages, int EmergencyMessages, int PendingVerifications);

// Account management for admins: list users and dashboard counts.
public class AdminService(AppDbContext db, PasswordResetService passwordResets)
{
    public Task<List<User>> UsersAsync(string? search, int limit)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim().ToLowerInvariant()}%";
            query = query.Where(u => EF.Functions.ILike(u.Name, term) || EF.Functions.ILike(u.Email, term));
        }
        return query
            .OrderByDescending(u => u.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync();
    }

    // Who uses the app and what is waiting for an admin.
    public async Task<AdminOverview> OverviewAsync()
    {
        var weekAgo = DateTime.UtcNow.AddDays(-7);
        var roles = await db.Users.GroupBy(u => u.Role).Select(g => new { Role = g.Key, Count = g.Count() }).ToListAsync();
        int CountOf(string role) => roles.FirstOrDefault(r => r.Role == role)?.Count ?? 0;
        return new AdminOverview(
            TotalUsers: roles.Sum(r => r.Count),
            Commuters: CountOf(UserRoles.Commuter),
            Drivers: CountOf(UserRoles.Driver),
            Admins: CountOf(UserRoles.Admin),
            NewThisWeek: await db.Users.CountAsync(u => u.CreatedAt >= weekAgo),
            PendingPasswordRequests: await passwordResets.PendingCountAsync(),
            UnreadMessages: await db.SupportMessages.CountAsync(m => !m.FromAdmin && m.ReadAt == null),
            EmergencyMessages: await db.SupportMessages.CountAsync(m => !m.FromAdmin && m.ReadAt == null && m.IsEmergency),
            PendingVerifications: await db.Verifications.CountAsync(v => v.Status == VerificationStatus.Hold));
    }
}
