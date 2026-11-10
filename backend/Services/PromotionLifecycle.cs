using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

public record PromotionLifecycleRun(int CampaignsExpired, int CampaignsAnnounced, int CashbackLotsExpired, int CouponReminders, int TripsProcessed);

// The timed side of F15, run every minute by PromotionLifecycleHostedService (and by hand in tests):
// ends campaigns whose end date passed, tells users about campaigns that just started (once each), expires
// cashback that was not used in time, reminds users of their coupons that expire within 3 days (once each), and
// processes completed trips whose rewards were missed.
public class PromotionLifecycle(AppDbContext db, CashbackService cashback, RewardEngine engine, NotificationService notifications)
{
    public static readonly TimeSpan ReminderWindow = TimeSpan.FromDays(3);

    public async Task<PromotionLifecycleRun> RunOnceAsync(DateTime now, CancellationToken ct = default)
    {
        var expired = await db.RewardCampaigns.Where(c => c.Status == CampaignStatus.Active && c.EndsAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CampaignStatus.Expired).SetProperty(c => c.UpdatedAt, now), ct);
        var announced = await AnnounceCampaignsAsync(now, ct);
        var lots = await cashback.ExpireDueAsync(now);
        var reminders = await RemindExpiringCouponsAsync(now, ct);
        var trips = await engine.CatchUpAsync(now);
        return new PromotionLifecycleRun(expired, announced, lots, reminders, trips);
    }

    private async Task<int> AnnounceCampaignsAsync(DateTime now, CancellationToken ct)
    {
        var due = await db.RewardCampaigns.AsNoTracking()
            .Where(c => c.Status == CampaignStatus.Active && c.AnnouncedAt == null && c.StartsAt <= now && c.EndsAt > now)
            .ToListAsync(ct);
        var count = 0;
        foreach (var c in due)
        {
            // Claiming the announcement first means two servers never both send it.
            var claimed = await db.RewardCampaigns.Where(x => x.Id == c.Id && x.AnnouncedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.AnnouncedAt, now), ct);
            if (claimed == 0)
            {
                continue;
            }
            var title = $"New offer: {c.Name}";
            if (title.Length > 150) title = title[..150];
            var ends = DhakaTime.FromUtc(c.EndsAt).ToString("d MMM");
            var message = $"{CampaignService.RewardText(c)} on Dhaka bus trips until {ends}. {c.Description}".Trim();
            if (message.Length > 1000) message = message[..1000];
            var rank = c.MinTier is null ? -1 : await db.MembershipTiers.Where(t => t.Code == c.MinTier).Select(t => t.Rank).FirstOrDefaultAsync(ct);
            // One notification per user (not admins), skipping anyone already told about this campaign.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO notifications (id, user_id, title, message, type, category, action_url, related_id, is_read, created_at)
                SELECT gen_random_uuid(), u.id, {title}, {message}, {NotificationTypes.Info}, {NotificationCategories.Reward}, {"/offers#campaigns"}, {c.Id}, false, {now}
                FROM users u
                WHERE u.role <> 'admin'
                  AND ({rank} < 0 OR EXISTS (SELECT 1 FROM user_memberships m JOIN membership_tiers t ON t.code = m.tier_code
                                            WHERE m.user_id = u.id AND t.rank >= {rank}))
                  AND NOT EXISTS (SELECT 1 FROM notifications n WHERE n.user_id = u.id AND n.related_id = {c.Id})
                """, ct);
            count++;
        }
        return count;
    }

    private async Task<int> RemindExpiringCouponsAsync(DateTime now, CancellationToken ct)
    {
        var until = now + ReminderWindow;
        var copies = await db.UserCoupons.Include(c => c.Coupon)
            .Where(c => c.UsedAt == null && c.ExpiryNotifiedAt == null && c.ExpiresAt != null && c.ExpiresAt > now && c.ExpiresAt <= until
                && c.Coupon!.IsActive && c.Coupon.DeletedAt == null)
            .OrderBy(c => c.ExpiresAt).Take(200)
            .ToListAsync(ct);
        foreach (var copy in copies)
        {
            copy.ExpiryNotifiedAt = now;
            var left = copy.ExpiresAt!.Value - now;
            var when = left.TotalHours < 24 ? "today" : $"in {Math.Ceiling(left.TotalDays)} days";
            notifications.Notify(copy.UserId, "Coupon expiring soon", $"Your coupon {copy.Coupon!.Code} ({PromotionRules.SavingText(copy.Coupon)}) expires {when}. Use it on a completed bus trip before then.",
                NotificationTypes.Warning, "/offers#coupons", NotificationCategories.Reward, relatedId: copy.Id);
        }
        await db.SaveChangesAsync(ct);
        return copies.Count;
    }
}

public class PromotionLifecycleHostedService(IServiceScopeFactory scopes, IOptions<PromotionOptions> options, ILogger<PromotionLifecycleHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.LifecycleEnabled)
        {
            return;
        }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(15, options.Value.LifecycleIntervalSeconds)));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PromotionLifecycle>().RunOnceAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Rewards lifecycle check failed; it will try again.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
