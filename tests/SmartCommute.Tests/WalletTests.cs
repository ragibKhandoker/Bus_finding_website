using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Tests;

[Collection(ServerCollection.Name)]
public class WalletTests(TestServerFixture server)
{
    private const int Rate = TestServerFixture.GemsPerTaka;

    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static Task<HttpResponseMessage> PostJson(HttpClient client, string path, object body) =>
        client.PostAsJsonAsync(path, body);

    // ---- wallet creation ----

    [SkippableFact]
    public async Task Signup_creates_exactly_one_wallet_with_zero_balances()
    {
        RequireServer();
        var anonymous = server.ClientFor(null);
        var email = $"{Guid.NewGuid():N}@test.local";
        var signup = await PostJson(anonymous, "/auth/signup", new { name = "New Rider", email, password = "Secret123" });
        Assert.Equal(HttpStatusCode.OK, signup.StatusCode);
        var token = (await JsonOf(signup)).GetProperty("token").GetString();

        var balance = await JsonOf(await server.ClientFor(token).GetAsync("/wallet/balance"));
        Assert.Equal(0m, balance.GetProperty("takaBalance").GetDecimal());
        Assert.Equal(0, balance.GetProperty("gemsBalance").GetInt32());

        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Wallets.CountAsync(w => w.User!.Email == email));
    }

    [SkippableFact]
    public async Task Backfill_creates_missing_wallets_once()
    {
        RequireServer();
        var (user, _) = await server.CreateUserAsync(withWallet: false);

        await using var scope = server.Scope();
        var wallets = scope.ServiceProvider.GetRequiredService<WalletService>();
        Assert.True(await wallets.CreateMissingWalletsAsync() >= 1);
        Assert.Equal(0, await wallets.CreateMissingWalletsAsync());

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var wallet = await db.Wallets.SingleAsync(w => w.UserId == user.Id);
        Assert.Equal(0m, wallet.Balance);
        Assert.Equal(0, wallet.Gems);
    }

    [SkippableFact]
    public async Task Simultaneous_first_requests_create_a_single_wallet()
    {
        RequireServer();
        var (user, client) = await server.CreateUserAsync(withWallet: false);
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.GetAsync("/wallet/balance")));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Wallets.CountAsync(w => w.UserId == user.Id));
    }

    // ---- gems to taka ----

    [SkippableFact]
    public async Task Convert_uses_whole_blocks_and_keeps_leftover_gems()
    {
        RequireServer();
        var (user, client) = await server.CreateUserAsync(balance: 5, gems: 27);

        var response = await PostJson(client, "/wallet/convert-gems", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal(20, body.GetProperty("gemsUsed").GetInt32());
        Assert.Equal(2m, body.GetProperty("convertedAmount").GetDecimal());
        Assert.Equal(7, body.GetProperty("gemsRemaining").GetInt32());

        var wallet = await server.WalletOfAsync(user.Id);
        Assert.Equal(7m, wallet.Balance);
        Assert.Equal(7, wallet.Gems);

        var entry = Assert.Single(await server.LedgerOfAsync(user.Id));
        Assert.Equal(WalletTransactionTypes.GemsConverted, entry.Type);
        Assert.Equal(2m, entry.Amount);
        Assert.Equal(-20, entry.GemsChange);
        Assert.Equal(7m, entry.BalanceAfter);
        Assert.Equal(7, entry.GemsAfter);
    }

    [SkippableFact]
    public async Task Convert_with_a_requested_amount_converts_only_whole_blocks_of_it()
    {
        RequireServer();
        var (user, client) = await server.CreateUserAsync(gems: 35);
        var response = await PostJson(client, "/wallet/convert-gems", new { gems = 25 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var wallet = await server.WalletOfAsync(user.Id);
        Assert.Equal(2m, wallet.Balance);
        Assert.Equal(15, wallet.Gems);
    }

    [SkippableFact]
    public async Task Convert_without_a_full_block_fails_and_changes_nothing()
    {
        RequireServer();
        var (user, client) = await server.CreateUserAsync(balance: 3, gems: Rate - 1);
        var response = await PostJson(client, "/wallet/convert-gems", new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False((await JsonOf(response)).GetProperty("success").GetBoolean());

        var wallet = await server.WalletOfAsync(user.Id);
        Assert.Equal(3m, wallet.Balance);
        Assert.Equal(Rate - 1, wallet.Gems);
        Assert.Empty(await server.LedgerOfAsync(user.Id));
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(1000)] // more gems than the user has
    public async Task Convert_rejects_invalid_gem_amounts(int gems)
    {
        RequireServer();
        var (user, client) = await server.CreateUserAsync(gems: 50);
        var response = await PostJson(client, "/wallet/convert-gems", new { gems });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(50, (await server.WalletOfAsync(user.Id)).Gems);
        Assert.Empty(await server.LedgerOfAsync(user.Id));
    }

    // ---- deductions ----

    [SkippableFact]
    public async Task Deduct_takes_the_amount_and_records_it()
    {
        RequireServer();
        var (user, client) = await server.CreateUserAsync(balance: 100);
        var response = await PostJson(client, "/wallet/deduct", new { amount = 30.5m, description = "Fare DBR-12" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(69.5m, (await JsonOf(response)).GetProperty("takaBalance").GetDecimal());

        Assert.Equal(69.5m, (await server.WalletOfAsync(user.Id)).Balance);
        var entry = Assert.Single(await server.LedgerOfAsync(user.Id));
        Assert.Equal(WalletTransactionTypes.Fare, entry.Type);
        Assert.Equal(-30.5m, entry.Amount);
        Assert.Equal(69.5m, entry.BalanceAfter);
        Assert.Equal("Fare DBR-12", entry.Description);
    }

    [SkippableFact]
    public async Task Deduct_more_than_the_balance_fails_and_changes_nothing()
    {
        RequireServer();
        var (user, client) = await server.CreateUserAsync(balance: 20);
        var response = await PostJson(client, "/wallet/deduct", new { amount = 20.01m });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Insufficient wallet balance.", (await JsonOf(response)).GetProperty("message").GetString());

        Assert.Equal(20m, (await server.WalletOfAsync(user.Id)).Balance);
        Assert.Empty(await server.LedgerOfAsync(user.Id));
    }

    [SkippableTheory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("1.234")]
    public async Task Deduct_rejects_invalid_amounts(string amount)
    {
        RequireServer();
        var (user, client) = await server.CreateUserAsync(balance: 50);
        var response = await PostJson(client, "/wallet/deduct", new { amount = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(50m, (await server.WalletOfAsync(user.Id)).Balance);
        Assert.Empty(await server.LedgerOfAsync(user.Id));
    }

    [SkippableFact]
    public async Task Simultaneous_deductions_never_spend_the_same_money_twice()
    {
        RequireServer();
        var (user, client) = await server.CreateUserAsync(balance: 100);
        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => PostJson(client, "/wallet/deduct", new { amount = 30 })));
        var succeeded = responses.Count(r => r.StatusCode == HttpStatusCode.OK);

        Assert.InRange(succeeded, 1, 3);
        var wallet = await server.WalletOfAsync(user.Id);
        Assert.Equal(100m - 30m * succeeded, wallet.Balance);
        Assert.True(wallet.Balance >= 0);
        var ledger = await server.LedgerOfAsync(user.Id);
        Assert.Equal(succeeded, ledger.Count);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK),
            r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.Conflict }));
    }

    [SkippableFact]
    public async Task Database_refuses_a_negative_balance()
    {
        RequireServer();
        var (user, _) = await server.CreateUserAsync(balance: 1);
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await Assert.ThrowsAnyAsync<Exception>(() =>
            db.Wallets.Where(w => w.UserId == user.Id).ExecuteUpdateAsync(s => s.SetProperty(w => w.Balance, -1m)));
        Assert.Equal(1m, (await server.WalletOfAsync(user.Id)).Balance);
    }

    // ---- history and access ----

    [SkippableFact]
    public async Task Transactions_are_paged_newest_first_with_balances_after()
    {
        RequireServer();
        var (_, client) = await server.CreateUserAsync(balance: 100);
        for (var i = 1; i <= 12; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await PostJson(client, "/wallet/deduct", new { amount = 1, description = $"Payment {i}" })).StatusCode);
        }

        var page1 = await JsonOf(await client.GetAsync("/wallet/transactions?page=1&pageSize=5"));
        Assert.Equal(12, page1.GetProperty("total").GetInt32());
        Assert.Equal(3, page1.GetProperty("totalPages").GetInt32());
        var first = page1.GetProperty("data")[0];
        Assert.Equal("Payment 12", first.GetProperty("description").GetString());
        Assert.Equal(-1m, first.GetProperty("amount").GetDecimal());
        Assert.Equal(88m, first.GetProperty("balanceAfter").GetDecimal());
        Assert.Equal(WalletTransactionTypes.Fare, first.GetProperty("type").GetString());
        Assert.True(first.TryGetProperty("createdAt", out _));

        var page3 = await JsonOf(await client.GetAsync("/wallet/transactions?page=3&pageSize=5"));
        Assert.Equal(2, page3.GetProperty("data").GetArrayLength());
    }

    [SkippableFact]
    public async Task Users_only_see_and_change_their_own_wallet()
    {
        RequireServer();
        var (alice, aliceClient) = await server.CreateUserAsync(balance: 40, gems: 30);
        var (bob, bobClient) = await server.CreateUserAsync(balance: 7);
        Assert.Equal(HttpStatusCode.OK, (await PostJson(aliceClient, "/wallet/deduct", new { amount = 10 })).StatusCode);

        // Bob sees his own balance and an empty ledger, never Alice's.
        var bobBalance = await JsonOf(await bobClient.GetAsync("/wallet/balance"));
        Assert.Equal(7m, bobBalance.GetProperty("takaBalance").GetDecimal());
        Assert.Equal(0, (await JsonOf(await bobClient.GetAsync("/wallet/transactions"))).GetProperty("total").GetInt32());

        // A user id in the body is not a way to reach someone else's wallet.
        await PostJson(bobClient, "/wallet/deduct", new { amount = 5, userId = alice.Id });
        Assert.Equal(30m, (await server.WalletOfAsync(alice.Id)).Balance);
        Assert.Equal(2m, (await server.WalletOfAsync(bob.Id)).Balance);

        // Creating wallets for everyone is for admins only.
        Assert.Equal(HttpStatusCode.Forbidden, (await bobClient.PostAsync("/wallet/initialize-all", null)).StatusCode);
    }

    [SkippableTheory]
    [InlineData("GET", "/wallet/balance")]
    [InlineData("GET", "/wallet/transactions")]
    [InlineData("POST", "/wallet/convert-gems")]
    [InlineData("POST", "/wallet/deduct")]
    public async Task Wallet_endpoints_require_sign_in(string method, string path)
    {
        RequireServer();
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { amount = 1 });
        }
        var response = await server.ClientFor(null).SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False((await JsonOf(response)).GetProperty("success").GetBoolean());
    }
}
