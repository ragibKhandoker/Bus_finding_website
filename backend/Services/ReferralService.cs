using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

public record ReferralSummary(string Code, int Invited, int Pending, int Qualified, int Rewarded, int Rejected, decimal Earned,
    decimal ReferrerReward, decimal ReferredReward, bool Enabled, bool CanApply, string? ReferredBy, string? ReferredStatus, int ApplyDays);

public record ReferralHistoryItem(Guid Id, string FriendName, string Status, DateTime CreatedAt, DateTime? RewardedAt, decimal Reward, string? Note);

public record ReferralResult(int StatusCode, string Message, Referral? Referral = null)
{
    public bool Succeeded => StatusCode is >= 200 and < 300;
}

// Refer & Earn. Every user has a referral code; a new user enters a friend's code at sign-up (or within
// ReferralApplyDays afterwards). Nothing is paid then: both people are rewarded by RewardEngine when the new user
// completes their first eligible trip. A person can be referred only once (unique referred_user_id), never by
// themselves (checked here and by a database constraint), and two people cannot refer each other.
public class ReferralService(AppDbContext db, RewardConfigService config, NotificationService notifications)
{
    public async Task<string> CodeAsync(Guid userId)
    {
        var code = await db.Users.Where(u => u.Id == userId).Select(u => u.ReferralCode).FirstOrDefaultAsync();
        if (code is not null)
        {
            return code;
        }
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var candidate = PromotionRules.NewReferralCode();
            try
            {
                // Only sets a code when the user still has none, so two first visits at once keep one code.
                var changed = await db.Users.Where(u => u.Id == userId && u.ReferralCode == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.ReferralCode, candidate));
                return changed == 1 ? candidate : (await db.Users.Where(u => u.Id == userId).Select(u => u.ReferralCode).FirstAsync())!;
            }
            catch (Exception ex) when ((ex as PostgresException ?? ex.InnerException as PostgresException)?.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                // Another user has this code already: try another.
            }
        }
        throw new InvalidOperationException("Could not create a unique referral code.");
    }

    // The referrer for a code typed at sign-up, or why it cannot be used. The account does not exist yet, so only
    // the code itself and whether the programme is on are checked.
    public async Task<(Guid? ReferrerId, string? Error)> ForSignupAsync(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return (null, null);
        }
        var settings = await config.SettingsAsync();
        if (!settings.ReferralEnabled)
        {
            return (null, "Refer & Earn is turned off right now, so referral codes cannot be used. Leave the field empty to sign up.");
        }
        var normalized = PromotionRules.NormalizeReferralCode(code);
        var referrer = normalized is null ? null : await db.Users.AsNoTracking().Where(u => u.ReferralCode == normalized).Select(u => (Guid?)u.Id).FirstOrDefaultAsync();
        return referrer is null ? (null, "That referral code does not exist. Check it with your friend, or leave the field empty.") : (referrer, null);
    }

    // Adds the referral for a new account; it is saved with the account.
    public Referral AddForNewUser(User user, Guid referrerId, string code)
    {
        var referral = new Referral { ReferrerId = referrerId, ReferredUser = user, ReferredUserId = user.Id, Code = PromotionRules.NormalizeReferralCode(code)! };
        db.Referrals.Add(referral);
        notifications.Notify(referrerId, "A friend joined with your code", $"{RewardEngine.FirstName(user.Name)} signed up with your referral code. You both get your reward after their first eligible bus trip.",
            NotificationTypes.Info, "/offers#refer", NotificationCategories.Reward);
        return referral;
    }

    // An existing user enters a friend's code.
    public async Task<ReferralResult> ApplyAsync(Guid userId, string? code)
    {
        var normalized = PromotionRules.NormalizeReferralCode(code);
        if (normalized is null)
        {
            return new(StatusCodes.Status400BadRequest, "Enter a referral code like DHK7K2M9Q.");
        }
        var settings = await config.SettingsAsync();
        var now = DateTime.UtcNow;
        var referrer = await db.Users.AsNoTracking().Where(u => u.ReferralCode == normalized).Select(u => new { u.Id, u.Name }).FirstOrDefaultAsync();
        if (referrer is null)
        {
            Audit(userId, "referral:unknown-code", normalized, now);
            await db.SaveChangesAsync();
            return new(StatusCodes.Status404NotFound, "That referral code does not exist.");
        }
        var me = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new
        {
            u.Name, u.CreatedAt,
            Referred = db.Referrals.Any(r => r.ReferredUserId == userId),
            Mutual = db.Referrals.Any(r => r.ReferrerId == userId && r.ReferredUserId == referrer.Id),
            Trips = db.TripRewards.Count(r => r.UserId == userId && r.Eligible),
        }).FirstAsync();
        if (PromotionRules.ReferralProblem(userId, referrer.Id, me.Referred, me.Mutual, me.CreatedAt, now, me.Trips, settings) is { } problem)
        {
            if (referrer.Id == userId || me.Mutual)
            {
                // Worth an admin's look: trying to refer yourself, or each other.
                Audit(userId, referrer.Id == userId ? "referral:self" : "referral:mutual", normalized, now);
                await db.SaveChangesAsync();
            }
            return new(referrer.Id == userId || me.Referred ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest, problem);
        }
        var referral = new Referral { ReferrerId = referrer.Id, ReferredUserId = userId, Code = normalized, CreatedAt = now };
        db.Referrals.Add(referral);
        notifications.Notify(referrer.Id, "A friend used your code", $"{RewardEngine.FirstName(me.Name)} entered your referral code. You both get your reward after their first eligible bus trip.",
            NotificationTypes.Info, "/offers#refer", NotificationCategories.Reward);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return new(StatusCodes.Status409Conflict, "You have already used a referral code.");
        }
        return new(StatusCodes.Status201Created, $"Code accepted. You and {RewardEngine.FirstName(referrer.Name)} will both be rewarded after your first eligible bus trip.", referral);
    }

    private void Audit(Guid userId, string action, string code, DateTime now) => db.RewardAuditLogs.Add(new RewardAuditLog
    {
        ActorId = userId, Action = action, EntityType = "referral", Details = $"Code {code}", CreatedAt = now,
    });

    public async Task<ReferralSummary> SummaryAsync(Guid userId)
    {
        var code = await CodeAsync(userId);
        var settings = await config.SettingsAsync();
        var now = DateTime.UtcNow;
        var row = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new
        {
            u.CreatedAt,
            Pending = db.Referrals.Count(r => r.ReferrerId == userId && r.Status == ReferralStatus.Pending),
            Qualified = db.Referrals.Count(r => r.ReferrerId == userId && r.Status == ReferralStatus.Qualified),
            Rewarded = db.Referrals.Count(r => r.ReferrerId == userId && r.Status == ReferralStatus.Rewarded),
            Rejected = db.Referrals.Count(r => r.ReferrerId == userId && r.Status == ReferralStatus.Rejected),
            Earned = db.CashbackTransactions.Where(c => c.UserId == userId && c.Source == CashbackSources.Referral).Sum(c => (decimal?)c.Amount) ?? 0,
            By = db.Referrals.Where(r => r.ReferredUserId == userId).Select(r => new { r.Referrer!.Name, r.Status }).FirstOrDefault(),
            Trips = db.TripRewards.Count(r => r.UserId == userId && r.Eligible),
        }).FirstAsync();
        var canApply = row.By is null && settings.ReferralEnabled && row.Trips == 0 && now - row.CreatedAt <= TimeSpan.FromDays(settings.ReferralApplyDays);
        return new ReferralSummary(code, row.Pending + row.Qualified + row.Rewarded + row.Rejected, row.Pending, row.Qualified, row.Rewarded, row.Rejected,
            row.Earned, settings.ReferrerReward, settings.ReferredReward, settings.ReferralEnabled, canApply,
            row.By is null ? null : RewardEngine.FirstName(row.By.Name), row.By?.Status, settings.ReferralApplyDays);
    }

    // The people this user invited, newest first. Only first names are shown.
    public async Task<List<ReferralHistoryItem>> HistoryAsync(Guid userId, int take = 50)
    {
        var rows = await db.Referrals.AsNoTracking().Where(r => r.ReferrerId == userId)
            .OrderByDescending(r => r.CreatedAt).Take(take)
            .Select(r => new { r.Id, r.ReferredUser!.Name, r.Status, r.CreatedAt, r.RewardedAt, r.ReferrerReward, r.Note })
            .ToListAsync();
        return rows.Select(r => new ReferralHistoryItem(r.Id, RewardEngine.FirstName(r.Name), r.Status, r.CreatedAt, r.RewardedAt, r.ReferrerReward, r.Note)).ToList();
    }
}
