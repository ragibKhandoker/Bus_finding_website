using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Tests;

public class LoginRoleMessageTests
{
    [Theory]
    [InlineData(UserRoles.Commuter, "This is a commuter account. Choose Commuter to sign in.")]
    [InlineData(UserRoles.Driver, "This is a driver account. Choose Driver to sign in.")]
    [InlineData(UserRoles.Admin, "This is an admin account. Choose Admin to sign in.")]
    public void Message_names_the_right_button(string role, string message) =>
        Assert.Equal(message, AccountService.WrongRoleMessage(role));
}

// Each account signs in only through the role it registered with.
[Collection(ServerCollection.Name)]
public class LoginRoleTests(TestServerFixture server)
{
    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    private async Task<User> AccountWithRole(string role)
    {
        var (user, _) = role == UserRoles.Admin ? await server.CreateAdminAsync() : await server.CreateUserAsync();
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Users.SingleAsync(u => u.Id == user.Id);
        stored.Role = role;
        stored.PasswordHash = PasswordHashing.Hash("secret123");
        await db.SaveChangesAsync();
        return stored;
    }

    private async Task<int> SessionsOf(Guid userId)
    {
        await using var scope = server.Scope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserSessions.CountAsync(s => s.UserId == userId);
    }

    // Sign-in is rate limited per client address (10 a minute), so each test sign-in comes from its own address,
    // as separate people's would. The limit itself stays in force.
    private HttpClient NewVisitor()
    {
        var client = server.ClientFor(null);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.{Random.Shared.Next(256)}.{Random.Shared.Next(256)}.{Random.Shared.Next(1, 255)}");
        return client;
    }

    private Task<HttpResponseMessage> Login(User user, string? role) =>
        NewVisitor().PostAsJsonAsync("/auth/login", new { email = user.Email, password = "secret123", role });

    [SkippableTheory]
    [InlineData(UserRoles.Commuter, UserRoles.Driver)]
    [InlineData(UserRoles.Commuter, UserRoles.Admin)]
    [InlineData(UserRoles.Driver, UserRoles.Commuter)]
    [InlineData(UserRoles.Driver, UserRoles.Admin)]
    [InlineData(UserRoles.Admin, UserRoles.Commuter)]
    [InlineData(UserRoles.Admin, UserRoles.Driver)]
    public async Task Wrong_role_button_is_refused_and_no_session_is_made(string accountRole, string chosenRole)
    {
        RequireServer();
        var user = await AccountWithRole(accountRole);
        var before = await SessionsOf(user.Id);

        var response = await Login(user, chosenRole);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(AccountService.WrongRoleMessage(accountRole), body.GetProperty("message").GetString());
        Assert.False(body.TryGetProperty("token", out _));
        Assert.Equal(before, await SessionsOf(user.Id));
    }

    [SkippableTheory]
    [InlineData(UserRoles.Commuter)]
    [InlineData(UserRoles.Driver)]
    [InlineData(UserRoles.Admin)]
    public async Task Right_role_button_signs_in(string role)
    {
        RequireServer();
        var user = await AccountWithRole(role);
        var response = await Login(user, role.ToUpperInvariant()); // the role is matched without regard to case
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(role, body.GetProperty("user").GetProperty("role").GetString());
    }

    [SkippableFact]
    public async Task Wrong_password_says_nothing_about_the_role_and_bad_roles_are_rejected()
    {
        RequireServer();
        var user = await AccountWithRole(UserRoles.Driver);
        var wrong = await NewVisitor().PostAsJsonAsync("/auth/login", new { email = user.Email, password = "nope12345", role = "commuter" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.DoesNotContain("driver", await wrong.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, (await Login(user, "superuser")).StatusCode);
    }
}
