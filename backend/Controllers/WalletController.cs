using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// The signed-in user's own wallet. The user always comes from the token; there is no way to name another user.
// The business rules live in WalletService.
[Authorize]
[Route("wallet")]
public class WalletController(WalletService wallets) : ApiControllerBase
{
    [HttpGet("balance")]
    public async Task<IActionResult> Balance(CancellationToken ct)
    {
        var wallet = await wallets.GetWalletAsync(CurrentUserId, ct);
        return Ok(new
        {
            success = true,
            message = "Wallet balance retrieved successfully",
            balance = wallet.Balance,
            gems = wallet.Gems,
            takaBalance = wallet.Balance,
            gemsBalance = wallet.Gems,
            conversion = Conversion(wallet),
            wallet = WalletViewModel.From(wallet),
        });
    }

    [HttpGet("test")]
    public async Task<IActionResult> Test(CancellationToken ct)
    {
        var wallet = await wallets.GetWalletAsync(CurrentUserId, ct);
        return Ok(new { success = true, message = "Wallet API is working", userId = CurrentUserId, wallet = WalletViewModel.From(wallet) });
    }

    // Creates wallets for any users that do not have one yet. The server also does this at startup.
    [Authorize(Roles = UserRoles.Admin)]
    [HttpPost("initialize-all")]
    public async Task<IActionResult> InitializeAll(CancellationToken ct)
    {
        var created = await wallets.CreateMissingWalletsAsync(ct);
        return Ok(new { success = true, message = $"Created {created} wallet(s).", created });
    }

    // Body (optional): { "gems": 120 }. Without it every whole block of GemsPerTaka gems is converted.
    [HttpPost("convert-gems")]
    public async Task<IActionResult> ConvertGems([FromBody] ConvertGemsRequest? request, CancellationToken ct)
    {
        var result = await wallets.ConvertGemsAsync(CurrentUserId, request?.Gems, ct);
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        var wallet = result.Wallet!;
        var entry = result.Transaction!;
        return Ok(new
        {
            success = true,
            message = result.Message,
            balance = wallet.Balance,
            gems = wallet.Gems,
            takaBalance = wallet.Balance,
            gemsBalance = wallet.Gems,
            convertedAmount = entry.Amount,
            gemsUsed = -entry.GemsChange,
            gemsRemaining = wallet.Gems,
            gemsPerTaka = wallets.Rewards.GemsPerTaka,
            conversion = Conversion(wallet),
            transaction = WalletTransactionViewModel.From(entry),
        });
    }

    // Pays from the caller's own wallet only. Body: { "amount": 25, "description": "Fare DBR-12" }.
    [HttpPost("deduct")]
    public async Task<IActionResult> Deduct(DeductRequest request, CancellationToken ct)
    {
        var result = await wallets.DeductAsync(CurrentUserId, request.Amount, request.Description, ct);
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        var wallet = result.Wallet!;
        return Ok(new
        {
            success = true,
            message = result.Message,
            newBalance = wallet.Balance,
            balance = wallet.Balance,
            gems = wallet.Gems,
            takaBalance = wallet.Balance,
            gemsBalance = wallet.Gems,
            transaction = WalletTransactionViewModel.From(result.Transaction!),
        });
    }

    // The caller's ledger, newest first: ?page=1&pageSize=20 (pageSize at most 100).
    [HttpGet("transactions")]
    public async Task<IActionResult> Transactions([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (items, total) = await wallets.TransactionsAsync(CurrentUserId, page, pageSize, ct);
        return Ok(new
        {
            success = true,
            page,
            pageSize,
            total,
            totalPages = (int)Math.Ceiling(total / (double)pageSize),
            data = items.Select(WalletTransactionViewModel.From),
        });
    }

    // What converting all gems now would give, so the page can show it before the user confirms.
    private object Conversion(Wallet wallet)
    {
        var (gemsUsed, taka) = wallets.ConversionFor(wallet.Gems);
        return new { gemsPerTaka = wallets.Rewards.GemsPerTaka, convertibleGems = gemsUsed, takaYouWouldGet = taka, gemsLeftOver = wallet.Gems - gemsUsed };
    }
}
