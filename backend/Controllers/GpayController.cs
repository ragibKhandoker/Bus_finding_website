using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// In-app stored value only; no real payment provider is involved.
[Authorize]
[Route("gpay")]
public class GpayController(AppDbContext db, WalletService wallets) : ApiControllerBase
{
    private static readonly PasswordHasher<GpayAccount> PinHasher = new();

    // Creates the account and returns a 4-digit PIN once. Only its hash is stored.
    [HttpPost("register")]
    public async Task<IActionResult> Register()
    {
        if (await db.GpayAccounts.AnyAsync(a => a.UserId == CurrentUserId))
        {
            return BadRequestError("You already have a GPay account. Unlock it with your PIN.");
        }

        var pin = RandomNumberGenerator.GetInt32(0, 10000).ToString("D4");
        var account = new GpayAccount { UserId = CurrentUserId, Balance = wallets.Rewards.GpayStartingBalance };
        account.PinHash = PinHasher.HashPassword(account, pin);
        db.GpayAccounts.Add(account);
        await db.SaveChangesAsync();

        return Created201(new
        {
            success = true,
            message = "GPay account created. Save your PIN; it will not be shown again.",
            data = GpayViewModel.From(account, pin),
        });
    }

    [EnableRateLimiting("strict")]
    [HttpPost("login")]
    public async Task<IActionResult> Login(GpayLoginRequest request)
    {
        var account = await db.GpayAccounts.FirstOrDefaultAsync(a => a.UserId == CurrentUserId);
        if (account is null)
        {
            return NotFoundError("GPay account not found.");
        }
        if (PinHasher.VerifyHashedPassword(account, account.PinHash, request.Code) == PasswordVerificationResult.Failed)
        {
            // 400 rather than 401: the web client treats 401 as an expired sign-in and logs the user out.
            return BadRequestError("Incorrect PIN.");
        }
        return Ok(new { success = true, message = "GPay unlocked.", data = GpayViewModel.From(account, request.Code) });
    }

    [HttpGet("balance")]
    public async Task<IActionResult> Balance()
    {
        var account = await db.GpayAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == CurrentUserId);
        return account is null
            ? NotFoundError("GPay account not found.")
            : Ok(new { success = true, data = GpayViewModel.From(account) });
    }

    // Moves money from GPay into the commute wallet.
    [HttpPost("recharge")]
    public async Task<IActionResult> Recharge(AmountRequest request)
    {
        var account = await db.GpayAccounts.FirstOrDefaultAsync(a => a.UserId == CurrentUserId);
        if (account is null)
        {
            return NotFoundError("GPay account not found.");
        }
        if (account.Balance < request.Amount)
        {
            return BadRequestError("Insufficient GPay balance.");
        }

        account.Balance -= request.Amount;
        account.UpdatedAt = DateTime.UtcNow;
        var wallet = await wallets.GetOrCreateAsync(CurrentUserId);
        wallets.Credit(wallet, request.Amount, WalletTransactionTypes.Recharge, "Recharge from GPay");
        await db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = $"৳{request.Amount} added to your wallet.",
            data = GpayViewModel.From(account),
            wallet = WalletViewModel.From(wallet),
        });
    }

    // Pays directly from the GPay balance.
    [HttpPost("deduct")]
    public async Task<IActionResult> Deduct(AmountRequest request)
    {
        var account = await db.GpayAccounts.FirstOrDefaultAsync(a => a.UserId == CurrentUserId);
        if (account is null)
        {
            return NotFoundError("GPay account not found.");
        }
        if (account.Balance < request.Amount)
        {
            return BadRequestError("Insufficient GPay balance.");
        }

        account.Balance -= request.Amount;
        account.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { success = true, message = "Payment completed.", data = GpayViewModel.From(account) });
    }
}
