using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Tests;

// Unit tests for the image check; no database needed.
public class ImageSniffTests
{
    [Fact]
    public void Recognises_real_image_signatures_only()
    {
        Assert.Equal("image/jpeg", ImageStorageService.Sniff([0xFF, 0xD8, 0xFF, 0xE0, 0x00]));
        Assert.Equal("image/png", ImageStorageService.Sniff([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00]));
        Assert.Equal("image/webp", ImageStorageService.Sniff("RIFF\0\0\0\0WEBPVP8 "u8.ToArray()));
        Assert.Null(ImageStorageService.Sniff("%PDF-1.7"u8.ToArray()));
        Assert.Null(ImageStorageService.Sniff("<svg xmlns="u8.ToArray()));
        Assert.Null(ImageStorageService.Sniff([]));
    }
}

[Collection(ServerCollection.Name)]
public class GpayVerifyTests(TestServerFixture server)
{
    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, .. Enumerable.Repeat((byte)7, 200)];
    private static string DataUrl(string type, byte[] bytes) => $"data:{type};base64,{Convert.ToBase64String(bytes)}";

    private async Task<(User User, HttpClient Client, string Pin)> WithGpay(decimal? setBalance = null)
    {
        var (user, client) = await server.CreateUserAsync();
        var created = await client.PostAsync("/gpay/register", null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var pin = (await JsonOf(created)).GetProperty("data").GetProperty("displayCode").GetString()!;
        if (setBalance is { } balance)
        {
            await using var scope = server.Scope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().GpayAccounts.Where(a => a.UserId == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Balance, balance));
        }
        return (user, client, pin);
    }

    private async Task<(GpayAccount Account, Wallet Wallet, List<GpayTransaction> Ledger)> State(Guid userId)
    {
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.GpayAccounts.AsNoTracking().SingleAsync(a => a.UserId == userId),
            await db.Wallets.AsNoTracking().SingleAsync(w => w.UserId == userId),
            await db.GpayTransactions.AsNoTracking().Where(x => x.UserId == userId).ToListAsync());
    }

    private async Task<Guid> CompletedTrip(Guid userId, decimal fare)
    {
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var trip = new Trip { UserId = userId, BusName = "Test Paribahan", Source = "Mirpur 10", Destination = "Farmgate", Fare = fare, Status = TripStatus.Completed, EndedAt = DateTime.UtcNow };
        db.Trips.Add(trip);
        await db.SaveChangesAsync();
        return trip.Id;
    }

    // ---- GPay ----

    [SkippableFact]
    public async Task Account_starts_with_the_configured_balance_once()
    {
        RequireServer();
        decimal configured;
        await using (var scope = server.Scope())
        {
            configured = scope.ServiceProvider.GetRequiredService<GpayService>().StartingBalance;
        }
        var (user, client, pin) = await WithGpay();
        Assert.Matches("^\\d{4}$", pin);
        var state = await State(user.Id);
        Assert.Equal(configured, state.Account.Balance);
        Assert.Equal(GpayTransactionTypes.Opening, Assert.Single(state.Ledger).Type);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/gpay/register", null)).StatusCode);

        var balance = await JsonOf(await client.GetAsync("/gpay/balance"));
        Assert.Equal(configured, balance.GetProperty("data").GetProperty("balance").GetDecimal());
        Assert.Equal(JsonValueKind.Null, balance.GetProperty("data").GetProperty("displayCode").ValueKind);
    }

    [SkippableFact]
    public async Task Moving_money_to_the_wallet_needs_the_pin_and_is_recorded_on_both_sides()
    {
        RequireServer();
        var (user, client, pin) = await WithGpay(setBalance: 500);
        var wrong = pin == "0000" ? "1111" : "0000";

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/gpay/recharge", new { amount = 100, pin = wrong })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/gpay/recharge", new { amount = 100 })).StatusCode);
        foreach (var amount in new object[] { 0, -5, 600, 1.005m })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/gpay/recharge", new { amount, pin })).StatusCode);
        }
        Assert.Equal(500m, (await State(user.Id)).Account.Balance);

        var response = await client.PostAsJsonAsync("/gpay/recharge", new { amount = 120.50m, pin });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await State(user.Id);
        Assert.Equal(379.50m, state.Account.Balance);
        Assert.Equal(120.50m, state.Wallet.Balance);
        var entry = Assert.Single(state.Ledger, x => x.Type == GpayTransactionTypes.WalletRecharge);
        Assert.Equal(-120.50m, entry.Amount);
        Assert.NotNull(entry.WalletTransactionId);
    }

    [SkippableFact]
    public async Task A_trip_is_paid_once_for_its_recorded_fare()
    {
        RequireServer();
        var (user, client, pin) = await WithGpay(setBalance: 100);
        var trip = await CompletedTrip(user.Id, 35);

        var pay = await client.PostAsJsonAsync("/gpay/pay", new { tripId = trip, pin, amount = 1 });
        Assert.Equal(HttpStatusCode.OK, pay.StatusCode);
        Assert.Equal(-35m, (await JsonOf(pay)).GetProperty("transaction").GetProperty("amount").GetDecimal());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/gpay/pay", new { tripId = trip, pin })).StatusCode);
        Assert.Equal(65m, (await State(user.Id)).Account.Balance);

        var trips = (await JsonOf(await client.GetAsync("/gpay/trips"))).GetProperty("data").EnumerateArray().ToList();
        Assert.True(Assert.Single(trips).GetProperty("paid").GetBoolean());

        var expensive = await CompletedTrip(user.Id, 70);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/gpay/pay", new { tripId = expensive, pin })).StatusCode);
        Assert.Equal(65m, (await State(user.Id)).Account.Balance);
    }

    [SkippableFact]
    public async Task Repeated_and_simultaneous_payments_never_overspend()
    {
        RequireServer();
        var (user, client, pin) = await WithGpay(setBalance: 100);
        var reference = Guid.NewGuid().ToString();
        var repeats = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync("/gpay/deduct", new { amount = 10, pin, reference })));
        Assert.All(repeats, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(90m, (await State(user.Id)).Account.Balance);

        var rush = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.PostAsJsonAsync("/gpay/deduct", new { amount = 40, pin })));
        Assert.Equal(2, rush.Count(r => r.StatusCode == HttpStatusCode.OK));
        var state = await State(user.Id);
        Assert.Equal(10m, state.Account.Balance);
        // The ledger records exactly what left the account: 10 once, then 40 twice.
        Assert.Equal(-90m, state.Ledger.Where(x => x.Type != GpayTransactionTypes.Opening).Sum(x => x.Amount));
    }

    [SkippableFact]
    public async Task Users_see_only_their_own_gpay_and_only_admins_top_up()
    {
        RequireServer();
        var (user, client, _) = await WithGpay(setBalance: 0);
        var (_, otherClient) = await server.CreateUserAsync();
        var (_, adminClient) = await server.CreateAdminAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync("/gpay/balance")).StatusCode);
        Assert.Empty((await JsonOf(await otherClient.GetAsync("/gpay/transactions"))).GetProperty("data").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/gpay/admin/top-up", new { userId = user.Id, amount = 500 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.ClientFor(null).GetAsync("/gpay/balance")).StatusCode);

        var body = new { userId = user.Id, amount = 250, reference = "test-topup-1" };
        Assert.Equal(HttpStatusCode.OK, (await adminClient.PostAsJsonAsync("/gpay/admin/top-up", body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await adminClient.PostAsJsonAsync("/gpay/admin/top-up", body)).StatusCode);
        Assert.Equal(250m, (await State(user.Id)).Account.Balance);
    }

    [SkippableFact]
    public async Task A_forgotten_pin_is_replaced_with_the_account_password()
    {
        RequireServer();
        var (user, client, oldPin) = await WithGpay(setBalance: 50);
        await using (var scope = server.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Users.SingleAsync(u => u.Id == user.Id);
            row.PasswordHash = new PasswordHasher<User>().HashPassword(row, "Secret123");
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/gpay/reset-pin", new { password = "wrong" })).StatusCode);
        var reset = await client.PostAsJsonAsync("/gpay/reset-pin", new { password = "Secret123" });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var newPin = (await JsonOf(reset)).GetProperty("data").GetProperty("displayCode").GetString();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/gpay/login", new { code = newPin })).StatusCode);
        if (newPin != oldPin)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/gpay/login", new { code = oldPin })).StatusCode);
        }
    }

    // ---- verification ----

    private static async Task<string> Upload(HttpClient client, byte[]? bytes = null, string type = "image/jpeg")
    {
        var response = await client.PostAsJsonAsync("/verify/upload-image", new { image = DataUrl(type, bytes ?? Jpeg) });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonOf(response)).GetProperty("imageId").GetString()!;
    }

    [SkippableFact]
    public async Task Only_real_images_within_the_limit_are_accepted()
    {
        RequireServer();
        var (_, client) = await server.CreateUserAsync();
        int max;
        await using (var scope = server.Scope())
        {
            max = scope.ServiceProvider.GetRequiredService<ImageStorageService>().MaxBytes;
        }

        await Upload(client);
        await Upload(client, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3], "image/png");
        string[] refused =
        [
            DataUrl("application/pdf", "%PDF-1.7 x"u8.ToArray()),
            DataUrl("image/png", "%PDF-1.7 x"u8.ToArray()),       // spoofed type
            DataUrl("image/png", Jpeg),                           // real JPEG declared as PNG
            DataUrl("image/svg+xml", "<svg></svg>"u8.ToArray()),
            DataUrl("image/jpeg", [0xFF, 0xD8, 0xFF, .. new byte[max]]), // too large
            "data:image/jpeg;base64,not base64!!",
            "data:image/jpeg;base64,",
            Convert.ToBase64String(Jpeg),                          // no data URL header
        ];
        foreach (var image in refused)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/verify/upload-image", new { image })).StatusCode);
        }
    }

    [SkippableFact]
    public async Task Documents_are_private_to_their_owner_and_admins()
    {
        RequireServer();
        var (_, owner) = await server.CreateUserAsync();
        var (_, other) = await server.CreateUserAsync();
        var (_, admin) = await server.CreateAdminAsync();
        var imageId = await Upload(owner);

        var mine = await owner.GetAsync($"/verify/images/{imageId}");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        Assert.Equal("image/jpeg", mine.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Jpeg, await mine.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/verify/images/{imageId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/verify/images/{imageId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await server.ClientFor(null).GetAsync($"/verify/images/{imageId}")).StatusCode);

        // Someone else's upload cannot be used in your request.
        Assert.Equal(HttpStatusCode.BadRequest, (await other.PostAsJsonAsync("/verify/submit", new { documentType = "nid", imageId })).StatusCode);
    }

    [SkippableFact]
    public async Task Submission_review_and_status_follow_the_rules()
    {
        RequireServer();
        var (user, client) = await server.CreateUserAsync();
        var (admin, adminClient) = await server.CreateAdminAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/verify/submit", new { documentType = "passport", imageId = await Upload(client) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/verify/submit", new { documentType = "student_pass", imageId = await Upload(client) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/verify/submit", new { documentType = "nid" })).StatusCode);

        var submit = await client.PostAsJsonAsync("/verify/submit", new { documentType = "student_pass", imageId = await Upload(client), institutionName = "BRAC University", institutionId = "22101234", status = "approved" });
        Assert.Equal(HttpStatusCode.Created, submit.StatusCode);
        var request = (await JsonOf(submit)).GetProperty("data");
        Assert.Equal("pending", request.GetProperty("state").GetString());
        var id = request.GetProperty("_id").GetString();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/verify/submit", new { documentType = "nid", imageId = await Upload(client) })).StatusCode);

        // Ordinary users cannot list or review.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/verify/all")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/verify/approve", new { verificationId = id })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/verify/reject", new { verificationId = id, reason = "no" })).StatusCode);

        var pending = (await JsonOf(await adminClient.GetAsync("/verify/all?status=pending&pageSize=100"))).GetProperty("data").EnumerateArray();
        Assert.Contains(pending, v => v.GetProperty("_id").GetString() == id);
        Assert.Equal(HttpStatusCode.BadRequest, (await adminClient.GetAsync("/verify/all?status=maybe")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await adminClient.PostAsJsonAsync("/verify/reject", new { verificationId = id, reason = " " })).StatusCode);
        var reject = await adminClient.PostAsJsonAsync("/verify/reject", new { verificationId = id, reason = "The ID card photo is blurry." });
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await adminClient.PostAsJsonAsync("/verify/approve", new { verificationId = id })).StatusCode);

        var status = await JsonOf(await client.GetAsync("/verify/my-status"));
        Assert.Equal("rejected", status.GetProperty("data").GetProperty("state").GetString());
        Assert.Equal("The ID card photo is blurry.", status.GetProperty("data").GetProperty("rejectionReason").GetString());

        // A rejected user may try again; approval marks them as a verified student, once.
        var again = await client.PostAsJsonAsync("/verify/submit", new { documentType = "student_pass", imageId = await Upload(client), institutionName = "BRAC University", institutionId = "22101234" });
        var againId = (await JsonOf(again)).GetProperty("data").GetProperty("_id").GetString();
        var approvals = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => adminClient.PostAsJsonAsync("/verify/approve", new { verificationId = againId })));
        Assert.Single(approvals, r => r.StatusCode == HttpStatusCode.OK);

        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True((await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).Pass);
        var reviewed = await db.Verifications.AsNoTracking().SingleAsync(v => v.Id == Guid.Parse(againId!));
        Assert.Equal(admin.Id, reviewed.ReviewedById);
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.UserId == user.Id && n.Title == "Student verification approved"));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/verify/submit", new { documentType = "student_pass", imageId = await Upload(client), institutionName = "X", institutionId = "1" })).StatusCode);
    }

    [SkippableFact]
    public async Task Approving_an_nid_marks_the_identity_verified()
    {
        RequireServer();
        var (user, client) = await server.CreateUserAsync();
        var (_, adminClient) = await server.CreateAdminAsync();
        var submit = await client.PostAsJsonAsync("/verify/submit", new { documentType = "nid", imageId = await Upload(client) });
        var id = (await JsonOf(submit)).GetProperty("data").GetProperty("_id").GetString();
        Assert.Equal(HttpStatusCode.OK, (await adminClient.PostAsJsonAsync("/verify/approve", new { verificationId = id })).StatusCode);

        var status = await JsonOf(await client.GetAsync("/verify/my-status"));
        Assert.True(status.GetProperty("nid").GetProperty("verified").GetBoolean());
        Assert.False(status.GetProperty("studentPass").GetProperty("verified").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync($"/verify/{id}/image")).StatusCode);
    }
}
