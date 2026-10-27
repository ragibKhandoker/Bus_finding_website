using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Tests;

// Runs the real server (all middleware, auth and EF Core migrations) against a throw-away PostgreSQL database.
//
// Set SMARTCOMMUTE_TEST_DB to a connection string for a PostgreSQL server where the user may create databases, e.g.
//   Host=localhost;Username=postgres;Password=...
// Each run creates a database named smartcommute_test_<random>, and drops it at the end. Without the variable the
// tests are skipped. The real database in appsettings.Local.json is never used: the connection string is
// overridden with an environment variable (which wins over that file), and the fixture refuses to run unless the
// server is connected to the database it just created.
public sealed class TestServerFixture : IAsyncLifetime
{
    public const string EnvVar = "SMARTCOMMUTE_TEST_DB";
    public const int GemsPerTaka = 10;

    private string? _adminConnection;
    private string? _databaseName;

    public WebApplicationFactory<Program>? Factory { get; private set; }

    public string SkipReason => $"Set {EnvVar} to a PostgreSQL connection string to run the wallet integration tests.";

    public async Task InitializeAsync()
    {
        _adminConnection = Environment.GetEnvironmentVariable(EnvVar);
        if (string.IsNullOrWhiteSpace(_adminConnection))
        {
            return;
        }

        _databaseName = $"smartcommute_test_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(_adminConnection))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        var testConnection = new NpgsqlConnectionStringBuilder(_adminConnection) { Database = _databaseName }.ConnectionString;
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", testConnection);
        Environment.SetEnvironmentVariable("Jwt__Secret", "integration-tests-only-secret-0123456789abcdef");
        Environment.SetEnvironmentVariable("Rewards__GemsPerTaka", GemsPerTaka.ToString());
        Environment.SetEnvironmentVariable("Admin__Email", "");
        Environment.SetEnvironmentVariable("Admin__Password", "");

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        _ = Factory.Server; // starts the app: migrations and seeding run here

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (db.Database.GetDbConnection().Database != _databaseName)
        {
            throw new InvalidOperationException("Refusing to run: the server is not connected to the temporary test database.");
        }
        var pending = await db.Database.GetPendingMigrationsAsync();
        if (pending.Any())
        {
            throw new InvalidOperationException($"Migrations did not apply: {string.Join(", ", pending)}");
        }
    }

    public async Task DisposeAsync()
    {
        Factory?.Dispose();
        if (_adminConnection is null || _databaseName is null)
        {
            return;
        }
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(_adminConnection);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }

    public AsyncServiceScope Scope() => Factory!.Services.CreateAsyncScope();

    // A signed-in user with a wallet holding the given balances. Created directly (not through /auth/signup,
    // which is rate limited), with a real session and token.
    public async Task<(User User, HttpClient Client)> CreateUserAsync(decimal balance = 0, int gems = 0, bool withWallet = true)
    {
        await using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<JwtTokenService>();
        var id = Guid.NewGuid().ToString("N");
        var user = new User { Name = "Test User", Email = $"{id}@test.local", FriendCode = id[..5].ToUpperInvariant(), PasswordHash = "x" };
        db.Users.Add(user);
        if (withWallet)
        {
            db.Wallets.Add(new Wallet { User = user, Balance = balance, Gems = gems });
        }
        var session = new UserSession { User = user, CreatedAt = DateTime.UtcNow, ExpiresAt = tokens.NewExpiry() };
        db.UserSessions.Add(session);
        await db.SaveChangesAsync();
        return (user, ClientFor(tokens.CreateToken(user, session)));
    }

    public HttpClient ClientFor(string? token)
    {
        var client = Factory!.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    public async Task<Wallet> WalletOfAsync(Guid userId)
    {
        await using var scope = Scope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Wallets.AsNoTracking().SingleAsync(w => w.UserId == userId);
    }

    public async Task<List<WalletTransaction>> LedgerOfAsync(Guid userId)
    {
        await using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.WalletTransactions.Where(t => t.Wallet!.UserId == userId).OrderBy(t => t.CreatedAt).AsNoTracking().ToListAsync();
    }
}

[CollectionDefinition(Name)]
public class ServerCollection : ICollectionFixture<TestServerFixture>
{
    public const string Name = "server";
}
