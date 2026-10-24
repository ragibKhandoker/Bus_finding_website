using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

// All balance and gem changes go through here so every change is recorded as a WalletTransaction.
// Callers are responsible for SaveChangesAsync.
public class WalletService(AppDbContext db, IOptions<RewardOptions> rewards)
{
    public RewardOptions Rewards { get; } = rewards.Value;

    public async Task<Wallet> GetOrCreateAsync(Guid userId)
    {
        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
        if (wallet is null)
        {
            wallet = new Wallet { UserId = userId };
            db.Wallets.Add(wallet);
        }
        return wallet;
    }

    public async Task<Offer> GetOrCreateOffersAsync(Guid userId)
    {
        var offer = await db.Offers.Include(o => o.Wallet).FirstOrDefaultAsync(o => o.UserId == userId);
        if (offer is null)
        {
            var wallet = await GetOrCreateAsync(userId);
            offer = new Offer
            {
                UserId = userId,
                Wallet = wallet,
                Cashback = Rewards.WelcomeCashback,
                Coupon = Rewards.WelcomeCoupon,
                Discount = Rewards.WelcomeDiscount,
            };
            db.Offers.Add(offer);
        }
        return offer;
    }

    public void Credit(Wallet wallet, decimal amount, string type, string description)
    {
        wallet.Balance += amount;
        Touch(wallet);
        Record(wallet, type, amount, 0, description);
    }

    public bool TryDebit(Wallet wallet, decimal amount, string type, string description)
    {
        if (wallet.Balance < amount)
        {
            return false;
        }
        wallet.Balance -= amount;
        Touch(wallet);
        Record(wallet, type, -amount, 0, description);
        return true;
    }

    public void AddGems(Wallet wallet, int gems, string description)
    {
        wallet.Gems += gems;
        Touch(wallet);
        Record(wallet, "gems-earned", 0, gems, description);
    }

    // Converts as many whole blocks of GemsPerTaka as possible. Returns (gemsUsed, takaAdded).
    public (int GemsUsed, decimal Amount) ConvertGems(Wallet wallet)
    {
        var blocks = wallet.Gems / Rewards.GemsPerTaka;
        if (blocks == 0)
        {
            return (0, 0);
        }
        var gemsUsed = blocks * Rewards.GemsPerTaka;
        wallet.Gems -= gemsUsed;
        wallet.Balance += blocks;
        Touch(wallet);
        Record(wallet, "gems-converted", blocks, -gemsUsed, $"Converted {gemsUsed} gems");
        return (gemsUsed, blocks);
    }

    private static void Touch(Wallet wallet)
    {
        wallet.LastUpdated = DateTime.UtcNow;
        wallet.UpdatedAt = DateTime.UtcNow;
    }

    private void Record(Wallet wallet, string type, decimal amount, int gems, string description)
    {
        db.WalletTransactions.Add(new WalletTransaction
        {
            Wallet = wallet,
            Type = type,
            Amount = amount,
            GemsChange = gems,
            Description = description.Length > 200 ? description[..200] : description,
        });
    }
}
