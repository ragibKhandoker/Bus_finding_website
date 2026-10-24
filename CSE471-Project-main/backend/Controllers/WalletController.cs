using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

[Authorize]
[Route("wallet")]
public class WalletController(AppDbContext db, WalletService wallets) : ApiControllerBase
{
    [HttpGet("balance")]
    public async Task<IActionResult> Balance()
    {
        var wallet = await wallets.GetOrCreateAsync(CurrentUserId);
        await db.SaveChangesAsync();
        return Ok(new
        {
            success = true,
            message = "Wallet balance retrieved successfully",
            balance = wallet.Balance,
            gems = wallet.Gems,
            wallet = WalletViewModel.From(wallet),
        });
    }

    [HttpGet("test")]
    public async Task<IActionResult> Test()
    {
        var wallet = await wallets.GetOrCreateAsync(CurrentUserId);
        await db.SaveChangesAsync();
        return Ok(new { success = true, message = "Wallet API is working", userId = CurrentUserId, wallet = WalletViewModel.From(wallet) });
    }

    // Creates wallets for any users that do not have one yet.
    [HttpPost("initialize-all")]
    public async Task<IActionResult> InitializeAll()
    {
        var missing = await db.Users.Where(u => u.Wallet == null).Select(u => u.Id).ToListAsync();
        db.Wallets.AddRange(missing.Select(id => new Wallet { UserId = id }));
        await db.SaveChangesAsync();
        return Ok(new { success = true, message = $"Created {missing.Count} wallet(s).", created = missing.Count });
    }

    [HttpPost("convert-gems")]
    public async Task<IActionResult> ConvertGems()
    {
        var wallet = await wallets.GetOrCreateAsync(CurrentUserId);
        var (gemsUsed, amount) = wallets.ConvertGems(wallet);
        if (gemsUsed == 0)
        {
            return BadRequestError($"You need at least {wallets.Rewards.GemsPerTaka} gems to convert.");
        }
        await db.SaveChangesAsync();
        return Ok(new
        {
            success = true,
            message = $"Converted {gemsUsed} gems to ৳{amount}.",
            balance = wallet.Balance,
            gems = wallet.Gems,
            convertedAmount = amount,
            gemsUsed,
        });
    }

    [HttpPost("deduct")]
    public async Task<IActionResult> Deduct(DeductRequest request)
    {
        var wallet = await wallets.GetOrCreateAsync(CurrentUserId);
        if (!wallets.TryDebit(wallet, request.Amount, "fare", request.Description ?? "Fare payment"))
        {
            return BadRequestError("Insufficient balance");
        }
        await db.SaveChangesAsync();
        return Ok(new
        {
            success = true,
            message = "Amount deducted.",
            newBalance = wallet.Balance,
            balance = wallet.Balance,
            gems = wallet.Gems,
        });
    }

    [HttpGet("transactions")]
    public async Task<IActionResult> Transactions()
    {
        var wallet = await wallets.GetOrCreateAsync(CurrentUserId);
        var items = await db.WalletTransactions
            .Where(t => t.WalletId == wallet.Id)
            .OrderByDescending(t => t.CreatedAt)
            .Take(100)
            .Select(t => new { _id = t.Id, t.Type, t.Amount, t.GemsChange, t.Description, t.CreatedAt })
            .ToListAsync();
        return Ok(new { success = true, data = items });
    }
}
