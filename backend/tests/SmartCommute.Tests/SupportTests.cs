using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Tests;

public class TemporaryPasswordUnitTests
{
    [Fact]
    public void Temporary_passwords_pass_the_password_rules_and_avoid_look_alikes()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = TemporaryPasswords.New();
            Assert.Equal(10, password.Length);
            Assert.Contains(password, char.IsLetter);
            Assert.Contains(password, char.IsDigit);
            Assert.DoesNotContain(password, c => "0O1lI".Contains(c));
        }
    }

    [Fact]
    public void Issuing_stores_only_a_bcrypt_hash_and_sets_the_flag_and_expiry()
    {
        var user = new User { Email = "a@test.local" };
        var now = new DateTime(2026, 11, 8, 10, 0, 0, DateTimeKind.Utc);
        var password = TemporaryPasswords.Issue(user, now, now.AddHours(24));

        Assert.True(PasswordHashing.IsBcrypt(user.PasswordHash));
        Assert.DoesNotContain(password, user.PasswordHash);
        Assert.True(user.MustChangePassword);
        Assert.Equal(now.AddHours(24), user.TemporaryPasswordExpiresAt);
        Assert.Equal(PasswordCheck.Ok, PasswordHashing.Verify(user, password));
        Assert.Equal(PasswordCheck.Failed, PasswordHashing.Verify(user, "wrong123"));
    }

    [Fact]
    public void Passwords_are_hashed_with_bcrypt_and_old_hashes_still_verify()
    {
        var user = new User { Email = "b@test.local" };
        user.PasswordHash = PasswordHashing.Hash("Secret123");
        Assert.StartsWith("$2", user.PasswordHash);
        Assert.Equal(60, user.PasswordHash.Length);
        Assert.Equal(PasswordCheck.Ok, PasswordHashing.Verify(user, "Secret123"));
        Assert.NotEqual(user.PasswordHash, PasswordHashing.Hash("Secret123")); // salted: never the same twice

        // An account from before bcrypt (ASP.NET Identity PBKDF2) still signs in, and is marked for a new hash.
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "Secret123");
        Assert.Equal(PasswordCheck.OkNeedsRehash, PasswordHashing.Verify(user, "Secret123"));
        Assert.Equal(PasswordCheck.Failed, PasswordHashing.Verify(user, "Wrong123"));

        Assert.True(PasswordHashing.IsTooLong(new string('a', 73)));
        Assert.False(PasswordHashing.IsTooLong(new string('a', 72)));
    }

    [Fact]
    public void Status_never_contains_a_password_and_reports_approved_then_expired()
    {
        var now = DateTime.UtcNow;
        var request = new PasswordResetRequest
        {
            Status = PasswordResetStatuses.Sent,
            TemporaryPasswordExpiresAt = now.AddHours(1),
            User = new User { Email = "a@test.local" },
        };
        Assert.DoesNotContain(typeof(ResetStatusViewModel).GetProperties(), p => p.Name.Contains("Password"));
        Assert.DoesNotContain(typeof(AdminResetRequestViewModel).GetProperties(), p => p.Name.Contains("Password"));
        Assert.Equal("approved", ResetStatusViewModel.From(request, now).Status);
        Assert.Equal("expired", ResetStatusViewModel.From(request, now.AddHours(2)).Status);
        request.Status = PasswordResetStatuses.Pending;
        Assert.Equal("pending", ResetStatusViewModel.From(request, now).Status);
    }

    [Fact]
    public void Secret_hash_is_stable_and_hex()
    {
        var hash = PasswordResetService.HashSecret("secret");
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, PasswordResetService.HashSecret("secret"));
        Assert.NotEqual(hash, PasswordResetService.HashSecret("secret2"));
    }
}

[Collection(ServerCollection.Name)]
public class PasswordResetFlowTests(TestServerFixture server)
{
    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task SetPassword(Guid userId, string password, bool legacy = false)
    {
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        user.PasswordHash = legacy ? new PasswordHasher<User>().HashPassword(user, password) : PasswordHashing.Hash(password);
        await db.SaveChangesAsync();
    }

    private async Task<string> StoredHash(Guid userId)
    {
        await using var scope = server.Scope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(u => u.Id == userId).Select(u => u.PasswordHash).SingleAsync();
    }

    private static string Raw(JsonElement element) => element.GetRawText();

    [SkippableFact]
    public async Task Admin_approves_without_seeing_a_password_and_only_the_requester_gets_one()
    {
        RequireServer();
        var (user, _) = await server.CreateUserAsync();
        await SetPassword(user.Id, "oldpass123");
        var (_, admin) = await server.CreateAdminAsync();
        var anonymous = server.ClientFor(null);

        // The user asks for help without signing in.
        var created = await anonymous.PostAsJsonAsync("/auth/forgot-password", new { email = user.Email, message = "Lost it" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await JsonOf(created);
        var requestId = body.GetProperty("requestId").GetString()!;
        var secret = body.GetProperty("secret").GetString()!;

        // Nothing to collect before an admin approves.
        Assert.Equal(HttpStatusCode.Conflict, (await anonymous.PostAsJsonAsync("/auth/forgot-password/reveal", new { requestId, secret })).StatusCode);

        // The admin approves; no password appears anywhere in what the admin receives.
        var list = await admin.GetAsync("/admin/password-requests");
        Assert.DoesNotContain("temporaryPassword", await list.Content.ReadAsStringAsync());
        var approved = await admin.PostAsync($"/admin/password-requests/{requestId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.DoesNotContain("temporaryPassword", await approved.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/admin/password-requests/{requestId}/approve", null)).StatusCode);
        // Approving alone changes nothing about the account yet.
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/auth/login", new { email = user.Email, password = "oldpass123" })).StatusCode);

        // The status says approved, still without a password; a wrong secret gets nothing.
        var status = await JsonOf(await anonymous.PostAsJsonAsync("/auth/forgot-password/status", new { requestId, secret }));
        Assert.Equal("approved", status.GetProperty("request").GetProperty("status").GetString());
        Assert.DoesNotContain("temporaryPassword", Raw(status));
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.PostAsJsonAsync("/auth/forgot-password/reveal", new { requestId, secret = "nope" })).StatusCode);

        // The requester collects it: made now, stored only as a bcrypt hash.
        var revealed = await anonymous.PostAsJsonAsync("/auth/forgot-password/reveal", new { requestId, secret });
        Assert.Equal(HttpStatusCode.OK, revealed.StatusCode);
        Assert.Equal("no-store", revealed.Headers.CacheControl?.ToString());
        var temporary = (await JsonOf(revealed)).GetProperty("temporaryPassword").GetString()!;
        var hash = await StoredHash(user.Id);
        Assert.True(PasswordHashing.IsBcrypt(hash));
        Assert.DoesNotContain(temporary, hash);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/auth/login", new { email = user.Email, password = "oldpass123" })).StatusCode);

        // Collecting again makes a new one; the previous one stops working.
        var again = (await JsonOf(await anonymous.PostAsJsonAsync("/auth/forgot-password/reveal", new { requestId, secret }))).GetProperty("temporaryPassword").GetString()!;
        Assert.NotEqual(temporary, again);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/auth/login", new { email = user.Email, password = temporary })).StatusCode);

        // Signing in with it works, but only the password change is allowed until a new password is set.
        var login = await JsonOf(await anonymous.PostAsJsonAsync("/auth/login", new { email = user.Email, password = again }));
        Assert.True(login.GetProperty("user").GetProperty("mustChangePassword").GetBoolean());
        var client = server.ClientFor(login.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/support")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/auth/password", new { currentPassword = again, newPassword = again })).StatusCode);

        var changed = await client.PutAsJsonAsync("/auth/password", new { currentPassword = again, newPassword = "newpass456" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.False((await JsonOf(changed)).GetProperty("user").GetProperty("mustChangePassword").GetBoolean());
        Assert.True(PasswordHashing.IsBcrypt(await StoredHash(user.Id)));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/support")).StatusCode);

        // The request is closed; nothing more can be collected from it.
        var done = await JsonOf(await anonymous.PostAsJsonAsync("/auth/forgot-password/status", new { requestId, secret }));
        Assert.Equal("completed", done.GetProperty("request").GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await anonymous.PostAsJsonAsync("/auth/forgot-password/reveal", new { requestId, secret })).StatusCode);
    }

    [SkippableFact]
    public async Task Sign_up_stores_bcrypt_and_old_hashes_become_bcrypt_at_sign_in()
    {
        RequireServer();
        var (user, _) = await server.CreateUserAsync();
        await SetPassword(user.Id, "legacy123", legacy: true);
        Assert.False(PasswordHashing.IsBcrypt(await StoredHash(user.Id)));

        var login = await server.ClientFor(null).PostAsJsonAsync("/auth/login", new { email = user.Email, password = "legacy123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True(PasswordHashing.IsBcrypt(await StoredHash(user.Id)));
        Assert.Equal(HttpStatusCode.OK, (await server.ClientFor(null).PostAsJsonAsync("/auth/login", new { email = user.Email, password = "legacy123" })).StatusCode);
    }

    [SkippableFact]
    public async Task Admins_cannot_reset_a_password_directly_any_more()
    {
        RequireServer();
        var (user, _) = await server.CreateUserAsync();
        var (_, admin) = await server.CreateAdminAsync();
        var response = await admin.PostAsync($"/admin/users/{user.Id}/reset-password", null);
        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
    }

    [SkippableFact]
    public async Task Expired_temporary_password_cannot_sign_in()
    {
        RequireServer();
        var (user, _) = await server.CreateUserAsync();
        string temporary;
        await using (var scope = server.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.Users.SingleAsync(u => u.Id == user.Id);
            temporary = TemporaryPasswords.Issue(stored, DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-1));
            await db.SaveChangesAsync();
        }
        var login = await server.ClientFor(null).PostAsJsonAsync("/auth/login", new { email = user.Email, password = temporary });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Contains("expired", (await JsonOf(login)).GetProperty("message").GetString());
    }

    [SkippableFact]
    public async Task A_newer_request_replaces_a_waiting_one_and_dismissed_requests_show_the_reason()
    {
        RequireServer();
        var (user, _) = await server.CreateUserAsync();
        var (_, admin) = await server.CreateAdminAsync();
        var anonymous = server.ClientFor(null);

        var first = await JsonOf(await anonymous.PostAsJsonAsync("/auth/forgot-password", new { email = user.Email }));
        var second = await JsonOf(await anonymous.PostAsJsonAsync("/auth/forgot-password", new { email = user.Email }));
        var firstStatus = await JsonOf(await anonymous.PostAsJsonAsync("/auth/forgot-password/status",
            new { requestId = first.GetProperty("requestId").GetString(), secret = first.GetProperty("secret").GetString() }));
        Assert.Equal("dismissed", firstStatus.GetProperty("request").GetProperty("status").GetString());

        var secondId = second.GetProperty("requestId").GetString();
        var dismissed = await admin.PostAsJsonAsync($"/admin/password-requests/{secondId}/dismiss", new { reason = "Call the office" });
        Assert.Equal(HttpStatusCode.OK, dismissed.StatusCode);
        var status = await JsonOf(await anonymous.PostAsJsonAsync("/auth/forgot-password/status",
            new { requestId = secondId, secret = second.GetProperty("secret").GetString() }));
        Assert.Equal("Call the office", status.GetProperty("request").GetProperty("adminNote").GetString());
    }

    [SkippableFact]
    public async Task Unknown_email_and_admin_accounts_cannot_request()
    {
        RequireServer();
        var (adminUser, _) = await server.CreateAdminAsync();
        var anonymous = server.ClientFor(null);
        Assert.Equal(HttpStatusCode.NotFound,
            (await anonymous.PostAsJsonAsync("/auth/forgot-password", new { email = "nobody@test.local" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await anonymous.PostAsJsonAsync("/auth/forgot-password", new { email = adminUser.Email })).StatusCode);
    }

    [SkippableFact]
    public async Task Only_admins_can_see_and_answer_requests()
    {
        RequireServer();
        var (_, client) = await server.CreateUserAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/password-requests")).StatusCode);
    }
}

[Collection(ServerCollection.Name)]
public class SupportMessageTests(TestServerFixture server)
{
    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private async Task<List<Notification>> NotificationsOf(Guid userId)
    {
        await using var scope = server.Scope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Notifications
            .Where(n => n.UserId == userId).AsNoTracking().ToListAsync();
    }

    [SkippableFact]
    public async Task User_and_admin_exchange_messages_with_unread_counts()
    {
        RequireServer();
        var (user, client) = await server.CreateUserAsync();
        var (adminUser, admin) = await server.CreateAdminAsync();

        var sent = await client.PostAsJsonAsync("/support/messages", new { text = "  Bus broke down near Farmgate  ", emergency = true });
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        Assert.Equal("Bus broke down near Farmgate", (await JsonOf(sent)).GetProperty("message").GetProperty("text").GetString());
        Assert.Contains(await NotificationsOf(adminUser.Id), n => n.Type == NotificationTypes.Error && n.SenderId == user.Id);

        var inbox = await JsonOf(await admin.GetAsync("/admin/support"));
        var thread = inbox.GetProperty("data").EnumerateArray().Single(t => t.GetProperty("user").GetProperty("_id").GetString() == user.Id.ToString());
        Assert.Equal(1, thread.GetProperty("emergencyUnread").GetInt32());

        Assert.Equal(HttpStatusCode.Created,
            (await admin.PostAsJsonAsync($"/admin/support/{user.Id}", new { text = "Help is on the way", emergency = false })).StatusCode);
        await admin.PostAsync($"/admin/support/{user.Id}/read", null);
        inbox = await JsonOf(await admin.GetAsync("/admin/support"));
        thread = inbox.GetProperty("data").EnumerateArray().Single(t => t.GetProperty("user").GetProperty("_id").GetString() == user.Id.ToString());
        Assert.Equal(0, thread.GetProperty("unread").GetInt32());

        Assert.Equal(1, (await JsonOf(await client.GetAsync("/support/unread"))).GetProperty("count").GetInt32());
        var mine = await JsonOf(await client.GetAsync("/support"));
        Assert.Equal(2, mine.GetProperty("messages").GetArrayLength());
        Assert.Equal("Admin", mine.GetProperty("messages")[1].GetProperty("senderName").GetString());
        await client.PostAsync("/support/read", null);
        Assert.Equal(0, (await JsonOf(await client.GetAsync("/support/unread"))).GetProperty("count").GetInt32());
    }

    [SkippableFact]
    public async Task Admin_can_start_a_conversation_and_blank_messages_are_refused()
    {
        RequireServer();
        var (user, client) = await server.CreateUserAsync();
        var (_, admin) = await server.CreateAdminAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/support/messages", new { text = "   " })).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await admin.PostAsJsonAsync($"/admin/support/{user.Id}", new { text = "Your account was verified", emergency = false })).StatusCode);
        Assert.Contains(await NotificationsOf(user.Id), n => n.ActionUrl == "/support");
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PostAsJsonAsync($"/admin/support/{Guid.NewGuid()}", new { text = "Hello" })).StatusCode);
    }

    [SkippableFact]
    public async Task Users_cannot_use_the_admin_inbox()
    {
        RequireServer();
        var (other, _) = await server.CreateUserAsync();
        var (_, client) = await server.CreateUserAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/admin/support/{other.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/admin/support/{other.Id}", new { text = "hi" })).StatusCode);
    }
}
