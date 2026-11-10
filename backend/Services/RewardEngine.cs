using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

// What processing one completed trip gave the rider.
public record TripRewardOutcome(bool Processed, bool Eligible, string Message, decimal Cashback = 0, decimal FirstTripBonus = 0,
    bool ReferralRewarded = false, string? TierBefore = null, string? TierAfter = null, int CouponsIssued = 0, string? Reason = null)
{
    public bool TierUpgraded => TierBefore is not null && TierAfter is not null && TierBefore != TierAfter;
}

// Turns a completed trip into its rewards: cashback (base rate, tier bonus and the best live campaign, within the
// limits), the first-trip bonus and coupons, the referral rewards for both people, and the rider's membership.
//
// It is triggered by the server's own end-trip workflow (BusController) and, for any trip that was missed, by the
// background job; there is no endpoint where a user can ask for rewards. The trip_rewards row (keyed by trip id) and
// the unique event keys make the whole thing idempotent: a trip is processed once, however often it is retried.
// Everything for one trip is saved in one transaction, so it either all happens or none of it does.
public class RewardEngine(
    AppDbContext db,
    OfferService offers,
    CashbackService cashback,
    CouponService coupons,
    RewardConfigService config,
    NotificationService notifications,
    ILogger<RewardEngine> logger)
{
    // For callers that must not fail because of rewards (the end-trip request): errors are logged and the
    // background job processes the trip later.
    public async Task<TripRewardOutcome?> ProcessTripSafelyAsync(Guid tripId)
    {
        try
        {
            return await ProcessTripAsync(tripId);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or NpgsqlException or TimeoutException)
        {
            logger.LogWarning(ex, "Rewards for trip {TripId} were not processed now; the background job will retry.", tripId);
            db.ChangeTracker.Clear();
            return null;
        }
    }

    public async Task<TripRewardOutcome> ProcessTripAsync(Guid tripId)
    {
        var trip = await db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripId);
        if (trip is null)
        {
            return new(false, false, "Trip not found.");
        }
        if (trip.Status != TripStatus.Completed)
        {
            return new(false, false, "The trip is not completed yet.");
        }
        if (await db.TripRewards.AsNoTracking().FirstOrDefaultAsync(r => r.TripId == tripId) is { } done)
        {
            return Already(done);
        }

        var settings = await config.SettingsAsync();
        var tiers = await config.TiersAsync();
        var userId = trip.UserId;
        var offer = await offers.EnsureAsync(userId);
        var referral = await db.Referrals.AsNoTracking().FirstOrDefaultAsync(r => r.ReferredUserId == userId && r.Status == ReferralStatus.Pending);
        var referrerOffer = referral is null ? null : await offers.EnsureAsync(referral.ReferrerId);
        var now = DateTime.UtcNow;
        var campaigns = await db.RewardCampaigns.AsNoTracking()
            .Where(c => c.Status == CampaignStatus.Active && c.StartsAt <= now && c.EndsAt > now && c.CashbackPercent + c.CashbackFixed > 0)
            .ToListAsync();
        var firstTripCoupons = await coupons.AutoIssuedAsync(CouponTypes.FirstTrip, now);
        var referralCoupons = referral is null ? [] : await coupons.AutoIssuedAsync(CouponTypes.Referral, now);

        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync();
                // One trip at a time per rider (and their referrer): the monthly limit, the first-trip check and the
                // membership count all depend on what was processed before.
                await coupons.LockUsersAsync(referral is null ? new[] { userId } : new[] { userId, referral.ReferrerId });
                if (await db.TripRewards.AsNoTracking().FirstOrDefaultAsync(r => r.TripId == tripId) is { } raced)
                {
                    await transaction.RollbackAsync();
                    return Already(raced);
                }
                var outcome = await ProcessLockedAsync(trip, settings, tiers, offer, referral, referrerOffer, campaigns, firstTripCoupons, referralCoupons, now);
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
                return outcome;
            });
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another request processed the trip (or paid the same event) first; nothing of this one was saved.
            db.ChangeTracker.Clear();
            var row = await db.TripRewards.AsNoTracking().FirstOrDefaultAsync(r => r.TripId == tripId);
            return row is null ? new(false, false, "These rewards were already given.") : Already(row);
        }
    }

    private static TripRewardOutcome Already(TripReward row) => new(false, row.Eligible,
        row.Eligible ? $"Rewards for this trip were already given ({PromotionRules.Money(row.Cashback)} cashback)." : row.Reason ?? "This trip does not earn rewards.",
        row.Cashback, Reason: row.Reason);

    private async Task<TripRewardOutcome> ProcessLockedAsync(Trip trip, RewardSettings settings, List<MembershipTier> tiers, Offer offer,
        Referral? referralSnapshot, Offer? referrerOffer, List<RewardCampaign> campaigns, List<Coupon> firstTripCoupons, List<Coupon> referralCoupons, DateTime now)
    {
        var userId = trip.UserId;
        var problem = PromotionRules.TripProblem(trip, settings.StartedAt);
        var record = new TripReward { TripId = trip.Id, UserId = userId, Fare = trip.Fare, ProcessedAt = now };
        db.TripRewards.Add(record);
        if (problem is not null)
        {
            record.Eligible = false;
            record.Reason = problem;
            return new(true, false, problem, Reason: problem);
        }
        record.Eligible = true;

        var eligibleBefore = await db.TripRewards.CountAsync(r => r.UserId == userId && r.Eligible);
        var createdAt = await db.Users.Where(u => u.Id == userId).Select(u => u.CreatedAt).FirstAsync();
        var membership = await db.UserMemberships.FirstOrDefaultAsync(m => m.UserId == userId);
        if (membership is null)
        {
            var spentBefore = await db.TripRewards.Where(r => r.UserId == userId && r.Eligible).SumAsync(r => (decimal?)r.Fare) ?? 0;
            membership = new UserMembership
            {
                UserId = userId, EligibleTrips = eligibleBefore, EligibleSpend = spentBefore,
                TierCode = PromotionRules.TierFor(tiers, eligibleBefore, spentBefore).Code,
            };
            db.UserMemberships.Add(membership);
        }
        var tierBefore = tiers.FirstOrDefault(t => t.Code == membership.TierCode) ?? PromotionRules.TierFor(tiers, membership.EligibleTrips, membership.EligibleSpend);
        record.TierCode = tierBefore.Code;

        // ---- Cashback ----
        var isNew = PromotionRules.IsNewUser(createdAt, now, settings, eligibleBefore);
        var earnedThisMonth = await cashback.EarnedThisMonthAsync(userId, now);
        var tripEnded = trip.EndedAt!.Value;
        RewardCampaign? best = null;
        var quote = PromotionRules.Cashback(trip.Fare, settings, tierBefore, null, earnedThisMonth);
        foreach (var c in campaigns)
        {
            if (PromotionRules.CampaignProblem(c, now, tripEnded, trip.Fare, tierBefore.Rank, tiers, isNew, eligibleBefore) is not null)
            {
                continue;
            }
            var withCampaign = PromotionRules.Cashback(trip.Fare, settings, tierBefore, c, earnedThisMonth, c.Budget is { } b ? b - c.Spent : null);
            if (withCampaign.CampaignAmount > 0 && withCampaign.Total > quote.Total)
            {
                best = c;
                quote = withCampaign;
            }
        }
        if (best is not null)
        {
            var amount = quote.CampaignAmount;
            var counted = await db.RewardCampaigns
                .Where(c => c.Id == best.Id && c.Status == CampaignStatus.Active && c.EndsAt > now
                    && (c.Budget == null || c.Spent + amount <= c.Budget) && (c.MaxRedemptions == null || c.Redemptions < c.MaxRedemptions))
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Spent, c => c.Spent + amount).SetProperty(c => c.Redemptions, c => c.Redemptions + 1)
                    .SetProperty(c => c.UpdatedAt, now));
            if (counted == 0)
            {
                // The campaign ran out a moment ago: the trip still earns the regular cashback.
                best = null;
                quote = PromotionRules.Cashback(trip.Fare, settings, tierBefore, null, earnedThisMonth);
            }
        }
        var route = $"{trip.BusName}, {trip.Source} to {trip.Destination}";
        if (quote.Total > 0)
        {
            await cashback.CreditAsync(userId, offer.Id, quote.Total, CashbackSources.Trip,
                best is null ? $"Trip on {route}" : $"Trip on {route} ({best.Name})",
                $"trip:{trip.Id}", settings, now, trip.Id, best?.Id, breakdown: quote, fare: trip.Fare);
        }
        record.Cashback = quote.Total;
        record.CampaignId = best?.Id;

        // ---- Membership ----
        membership.EligibleTrips = eligibleBefore + 1;
        membership.EligibleSpend += trip.Fare;
        membership.UpdatedAt = now;
        var tierAfter = PromotionRules.TierFor(tiers, membership.EligibleTrips, membership.EligibleSpend);
        if (tierAfter.Rank > tierBefore.Rank)
        {
            membership.TierCode = tierAfter.Code;
            membership.UpgradedAt = now;
            notifications.Notify(userId, $"You're now {tierAfter.Name}", $"Welcome to {tierAfter.Name} membership after {membership.EligibleTrips} eligible trips. {tierAfter.Benefits}",
                NotificationTypes.Success, "/offers", NotificationCategories.Reward);
        }

        // ---- First trip ----
        decimal firstBonus = 0;
        var issued = 0;
        if (eligibleBefore == 0)
        {
            if (settings.FirstTripCashback > 0
                && await cashback.CreditAsync(userId, offer.Id, settings.FirstTripCashback, CashbackSources.FirstTrip, "First eligible trip bonus",
                    $"first-trip:{userId}", settings, now, trip.Id) is { } bonus)
            {
                firstBonus = bonus.Amount;
            }
            foreach (var coupon in firstTripCoupons)
            {
                if (await coupons.IssueAsync(userId, coupon, CouponTypes.FirstTrip, $"first-trip:{coupon.Id}", now) is not null)
                {
                    issued++;
                }
            }
            if (issued > 0)
            {
                notifications.Notify(userId, "First trip reward", $"Thanks for your first trip! {issued} new coupon{(issued == 1 ? " is" : "s are")} waiting on the Offers page.",
                    NotificationTypes.Success, "/offers", NotificationCategories.Reward);
            }
        }

        // ---- Referral ----
        var referralRewarded = false;
        if (eligibleBefore == 0 && referralSnapshot is not null && referrerOffer is not null)
        {
            var referral = await db.Referrals.FirstOrDefaultAsync(r => r.Id == referralSnapshot.Id && r.Status == ReferralStatus.Pending);
            if (referral is not null)
            {
                referralRewarded = await RewardReferralAsync(referral, trip, settings, offer, referrerOffer, referralCoupons, now);
            }
        }

        var parts = new List<string>();
        if (quote.Total > 0) parts.Add($"{PromotionRules.Money(quote.Total)} cashback");
        if (firstBonus > 0) parts.Add($"a {PromotionRules.Money(firstBonus)} first-trip bonus");
        if (referralRewarded) parts.Add("your referral reward");
        var message = parts.Count == 0
            ? quote.LimitedBy == "monthly" ? "You reached this month's cashback limit." : "This trip did not earn cashback."
            : $"You earned {string.Join(" and ", parts)}.";
        return new(true, true, message, quote.Total, firstBonus, referralRewarded, tierBefore.Code, membership.TierCode, issued);
    }

    private async Task<bool> RewardReferralAsync(Referral referral, Trip trip, RewardSettings settings, Offer referredOffer, Offer referrerOffer,
        List<Coupon> referralCoupons, DateTime now)
    {
        referral.QualifyingTripId = trip.Id;
        referral.QualifiedAt = now;
        var monthStart = PromotionRules.MonthStartUtc(now);
        var rewardedThisMonth = await db.Referrals.CountAsync(r => r.ReferrerId == referral.ReferrerId && r.Status == ReferralStatus.Rewarded && r.RewardedAt >= monthStart);
        if (!settings.ReferralEnabled || rewardedThisMonth >= settings.MaxReferralRewardsPerMonth && settings.MaxReferralRewardsPerMonth > 0)
        {
            referral.Status = ReferralStatus.Qualified;
            referral.Note = settings.ReferralEnabled ? "The inviter reached this month's referral reward limit." : "Refer & Earn was turned off.";
            db.RewardAuditLogs.Add(new RewardAuditLog
            {
                Action = "referral:not-rewarded", EntityType = "referral", EntityId = referral.Id.ToString(), Details = referral.Note, CreatedAt = now,
            });
            return false;
        }
        var referredName = await db.Users.Where(u => u.Id == referral.ReferredUserId).Select(u => u.Name).FirstAsync();
        var referred = await cashback.CreditAsync(referral.ReferredUserId, referredOffer.Id, settings.ReferredReward, CashbackSources.Referral,
            "Referral reward for joining with a friend's code", $"referral:{referral.Id}:referred", settings, now, referralId: referral.Id);
        var referrer = await cashback.CreditAsync(referral.ReferrerId, referrerOffer.Id, settings.ReferrerReward, CashbackSources.Referral,
            $"Referral reward: {FirstName(referredName)} took their first trip", $"referral:{referral.Id}:referrer", settings, now, referralId: referral.Id);
        foreach (var coupon in referralCoupons)
        {
            await coupons.IssueAsync(referral.ReferredUserId, coupon, CouponTypes.Referral, $"referral:{referral.Id}:{coupon.Id}", now);
            await coupons.IssueAsync(referral.ReferrerId, coupon, CouponTypes.Referral, $"referral:{referral.Id}:{coupon.Id}", now);
        }
        referral.ReferrerReward = referrer?.Amount ?? 0;
        referral.ReferredReward = referred?.Amount ?? 0;
        referral.Status = ReferralStatus.Rewarded;
        referral.RewardedAt = now;
        return true;
    }

    // Only the first name is shown to the other person.
    public static string FirstName(string name) => (name ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Your friend";

    // Trips the end-trip request could not process (the server stopped, the database was busy): processed now.
    public async Task<int> CatchUpAsync(DateTime now, int batch = 20)
    {
        var settings = await config.SettingsAsync();
        var before = now.AddMinutes(-1);
        var ids = await db.Trips.AsNoTracking()
            .Where(t => t.Status == TripStatus.Completed && t.IndividualBusId != null && t.EndedAt != null && t.EndedAt >= settings.StartedAt && t.EndedAt <= before
                && !db.TripRewards.Any(r => r.TripId == t.Id))
            .OrderBy(t => t.EndedAt).Take(batch).Select(t => t.Id).ToListAsync();
        var processed = 0;
        foreach (var id in ids)
        {
            if (await ProcessTripSafelyAsync(id) is { Processed: true })
            {
                processed++;
            }
        }
        return processed;
    }
}
