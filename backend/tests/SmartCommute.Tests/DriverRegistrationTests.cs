using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Tests;

// F21: the driver application rules, the encryption of document numbers and the status workflow, without a database.
public class DriverRuleTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);
    private static readonly DriverVerificationOptions Defaults = new();

    private const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";

    private static DriverApplicationInput Valid() => new()
    {
        FullName = "Karim Uddin", Phone = "+880 1712-345678", DateOfBirth = "1990-05-01", Gender = "Male",
        LicenseNumber = "DK0123456L00001", LicenseType = "Professional", LicenseExpiryDate = "2030-01-01", DrivingExperienceYears = 8,
        NidNumber = "1990123456789", BusName = "Turag Paribahan", OperatorName = "Turag Transport", Route = "Gazipur – Jatrabari",
        EmploymentType = "Full-time", PresentAddress = "House 12, Road 3, Uttara", District = "Dhaka", Area = "Uttara",
    };

    [Theory]
    [InlineData("01712345678", "01712345678")]
    [InlineData("+880 1712-345678", "01712345678")]
    [InlineData("8801912345678", "01912345678")]
    [InlineData("(017) 1234 5678", "01712345678")]
    [InlineData("01212345678", null)] // 012 is not a mobile prefix
    [InlineData("0171234567", null)]
    [InlineData("hello", null)]
    [InlineData("", null)]
    public void Phone_numbers_are_normalised_to_the_local_form(string input, string? expected) =>
        Assert.Equal(expected, DriverRules.NormalizePhone(input));

    [Fact]
    public void A_complete_application_passes_every_section()
    {
        var r = Valid();
        var (personal, p1) = DriverRules.CheckPersonal(r, Today, 18);
        Assert.Null(p1);
        Assert.Equal("01712345678", personal!.Phone);
        Assert.Null(DriverRules.CheckDriving(r, Today, Defaults.LicensePattern, personal.DateOfBirth).Problem);
        Assert.Null(DriverRules.CheckNid(r, Defaults.NidPattern).Problem);
        Assert.Null(DriverRules.CheckEmployment(r, null).Problem);
        Assert.Null(DriverRules.CheckAddress(r).Problem);
    }

    [Fact]
    public void Underage_applicants_are_refused()
    {
        var r = Valid();
        r.DateOfBirth = "2009-01-01"; // 17 on 2026-11-11
        Assert.Equal("Drivers must be at least 18 years old.", DriverRules.CheckPersonal(r, Today, 18).Problem);
        r.DateOfBirth = "2008-11-11"; // exactly 18 today
        Assert.Null(DriverRules.CheckPersonal(r, Today, 18).Problem);
    }

    [Theory]
    [InlineData("", null, null, null)]                    // name required
    [InlineData("Karim", "12345", null, null)]            // bad phone
    [InlineData("Karim", null, "not-a-date", null)]       // bad date of birth
    [InlineData("Karim", null, "2030-01-01", null)]       // future date of birth
    [InlineData("Karim", null, null, "Robot")]            // gender not allowed
    public void Invalid_personal_information_is_refused(string name, string? phone, string? dob, string? gender)
    {
        var r = Valid();
        r.FullName = name;
        if (phone is not null) r.Phone = phone;
        if (dob is not null) r.DateOfBirth = dob;
        if (gender is not null) r.Gender = gender;
        Assert.NotNull(DriverRules.CheckPersonal(r, Today, 18).Problem);
    }

    [Theory]
    [InlineData("", "Professional", "2030-01-01", 5)]            // no licence number
    [InlineData("AB", "Professional", "2030-01-01", 5)]          // too short
    [InlineData("DK0123456L00001", "Learner", "2030-01-01", 5)]  // unknown licence type
    [InlineData("DK0123456L00001", "Professional", "2026-11-11", 5)] // expires today: not in the future
    [InlineData("DK0123456L00001", "Professional", "2020-01-01", 5)] // expired
    [InlineData("DK0123456L00001", "Professional", "2030-01-01", -1)] // negative experience
    [InlineData("DK0123456L00001", "Professional", "2030-01-01", 61)] // beyond the limit
    public void Invalid_licence_data_is_refused(string number, string type, string expiry, int years)
    {
        var r = Valid();
        r.LicenseNumber = number;
        r.LicenseType = type;
        r.LicenseExpiryDate = expiry;
        r.DrivingExperienceYears = years;
        Assert.NotNull(DriverRules.CheckDriving(r, Today, Defaults.LicensePattern).Problem);
    }

    [Fact]
    public void Experience_cannot_exceed_what_the_age_allows()
    {
        var r = Valid();
        r.DrivingExperienceYears = 30; // born 1990, 36 years old: at most 20 years
        Assert.NotNull(DriverRules.CheckDriving(r, Today, Defaults.LicensePattern, new DateOnly(1990, 5, 1)).Problem);
    }

    [Theory]
    [InlineData("1234567890", true)]
    [InlineData("1990123456789", true)]
    [InlineData("19901234567890123", true)]
    [InlineData("1990-1234-5678-9", true)] // dashes and spaces are ignored
    [InlineData("12345", false)]
    [InlineData("ABCDEFGHIJ", false)]
    [InlineData("", false)]
    public void Nid_numbers_follow_the_configured_rule(string nid, bool ok)
    {
        var r = Valid();
        r.NidNumber = nid;
        Assert.Equal(ok, DriverRules.CheckNid(r, Defaults.NidPattern).Problem is null);
    }

    [Fact]
    public void A_chosen_bus_takes_its_details_from_the_database_not_the_request()
    {
        var r = Valid();
        r.BusId = Guid.NewGuid();
        r.BusName = "Fake name";
        var bus = new Bus
        {
            Id = r.BusId.Value, BusName = "Raida", RouteNumber = "DBR-7", Operator = "Raida Enterprise",
            Stops = [new BusStop { Order = 0, Name = "Postogola" }, new BusStop { Order = 1, Name = "Tongi" }],
        };
        var (employment, problem) = DriverRules.CheckEmployment(r, bus);
        Assert.Null(problem);
        Assert.Equal("Raida", employment!.BusName);
        Assert.Equal("Raida Enterprise", employment.OperatorName);
        Assert.Equal(bus.Id, employment.BusId);
        Assert.Contains("Postogola", employment.Route);
        // A bus id that does not exist is refused rather than silently ignored.
        Assert.NotNull(DriverRules.CheckEmployment(r, null).Problem);
    }

    [Fact]
    public void Address_needs_a_real_district()
    {
        var r = Valid();
        r.District = "Atlantis";
        Assert.NotNull(DriverRules.CheckAddress(r).Problem);
        r.District = "dhaka";
        Assert.Equal("Dhaka", DriverRules.CheckAddress(r).Value!.District);
        Assert.Equal(64, DriverRules.Districts.Length);
    }

    [Fact]
    public void Required_documents_must_be_present()
    {
        var (_, problem) = DriverRules.CheckDocuments(new() { ["license_front"] = Png }, DriverDocumentTypes.All, DriverDocumentTypes.Required, 1024);
        Assert.Equal("Upload the front of your NID.", problem);
        var (docs, ok) = DriverRules.CheckDocuments(new() { ["license_front"] = Png, ["nid_front"] = Png }, DriverDocumentTypes.All, DriverDocumentTypes.Required, 1024);
        Assert.Null(ok);
        Assert.Equal(2, docs!.Count);
    }

    [Fact]
    public void Unsupported_and_fake_images_are_refused()
    {
        var gif = "data:image/gif;base64,R0lGODlhAQABAAAAACw=";
        Assert.Contains("JPEG, PNG or WebP", DriverRules.CheckDocuments(new() { ["license_front"] = gif, ["nid_front"] = Png },
            DriverDocumentTypes.All, DriverDocumentTypes.Required, 1024).Problem);
        // Says PNG, but the bytes are text.
        var fake = "data:image/png;base64," + Convert.ToBase64String("<script>alert(1)</script>"u8.ToArray());
        Assert.Contains("not a real", DriverRules.CheckDocuments(new() { ["license_front"] = fake, ["nid_front"] = Png },
            DriverDocumentTypes.All, DriverDocumentTypes.Required, 1024).Problem);
        Assert.Equal("Unknown document type.", DriverRules.CheckDocuments(new() { ["../../etc/passwd"] = Png, ["license_front"] = Png, ["nid_front"] = Png },
            DriverDocumentTypes.All, DriverDocumentTypes.Required, 1024).Problem);
    }

    [Fact]
    public void Oversized_images_are_refused()
    {
        var big = new byte[3000];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(big, 0);
        var data = "data:image/png;base64," + Convert.ToBase64String(big);
        Assert.Contains("at most", DriverRules.CheckDocuments(new() { ["license_front"] = data, ["nid_front"] = Png },
            DriverDocumentTypes.All, DriverDocumentTypes.Required, 2048).Problem);
    }

    [Theory]
    [InlineData("short", "short", "Password must be at least 8 characters.")]
    [InlineData("abcdefgh", "abcdefgh", "Password must contain at least one letter and one number.")]
    [InlineData("abcdefg1", "abcdefg2", "Passwords do not match.")]
    [InlineData("abcdefg1", "abcdefg1", null)]
    public void Passwords_must_be_strong_and_confirmed(string password, string confirm, string? expected) =>
        Assert.Equal(expected, DriverRules.PasswordProblem(password, confirm));

    [Theory]
    [InlineData("PENDING", "UNDER_REVIEW", true)]
    [InlineData("PENDING", "APPROVED", true)]
    [InlineData("UNDER_REVIEW", "REJECTED", true)]
    [InlineData("UNDER_REVIEW", "RESUBMISSION_REQUIRED", true)]
    [InlineData("RESUBMISSION_REQUIRED", "APPROVED", false)] // must be resubmitted first
    [InlineData("RESUBMISSION_REQUIRED", "PENDING", false)]  // only the driver's resubmission does that
    [InlineData("APPROVED", "REJECTED", false)]
    [InlineData("REJECTED", "APPROVED", false)]
    public void Only_the_allowed_admin_decisions_are_possible(string from, string to, bool allowed) =>
        Assert.Equal(allowed, DriverVerificationStatus.AdminCanMove(from, to));

    [Fact]
    public void Account_status_follows_the_verification_status()
    {
        Assert.Equal(DriverAccountStatus.PendingVerification, DriverVerificationStatus.AccountStatusFor(DriverVerificationStatus.Pending));
        Assert.Equal(DriverAccountStatus.Active, DriverVerificationStatus.AccountStatusFor(DriverVerificationStatus.Approved));
        Assert.Equal(DriverAccountStatus.Rejected, DriverVerificationStatus.AccountStatusFor(DriverVerificationStatus.Rejected));
    }

    private static SensitiveDataProtector Protector(string key = "") =>
        new(Options.Create(new DriverVerificationOptions { EncryptionKey = key }), Options.Create(new JwtOptions { Secret = "unit-test-secret-0123456789abcdef0123" }));

    [Fact]
    public void Document_numbers_are_encrypted_and_only_readable_with_the_key()
    {
        var protector = Protector();
        var cipher = protector.Encrypt("DK0123456L00001");
        Assert.StartsWith("v1:", cipher);
        Assert.DoesNotContain("DK0123456", cipher);
        Assert.NotEqual(cipher, protector.Encrypt("DK0123456L00001")); // a new nonce every time
        Assert.Equal("DK0123456L00001", protector.Decrypt(cipher));

        var other = Protector(Convert.ToBase64String(new byte[32]));
        Assert.Null(other.Decrypt(cipher));
        var tampered = cipher[..^4] + (cipher[^4] == 'A' ? "B" : "A") + cipher[^3..];
        Assert.Null(protector.Decrypt(tampered));
    }

    [Fact]
    public void Hashes_ignore_formatting_and_keep_purposes_apart()
    {
        var protector = Protector();
        Assert.Equal(protector.Hash("license", "dk-0123 456"), protector.Hash("license", "DK0123456"));
        Assert.NotEqual(protector.Hash("license", "1234567890"), protector.Hash("nid", "1234567890"));
        Assert.Equal(64, protector.Hash("nid", "1234567890").Length);
    }

    [Fact]
    public void Masks_show_only_the_last_four()
    {
        Assert.Equal("6789", SensitiveDataProtector.Last4("1990123456789"));
        Assert.Equal("••••••6789", SensitiveDataProtector.Mask("6789"));
        Assert.Equal("••••••••", SensitiveDataProtector.Mask(""));
    }

    [Fact]
    public void A_bad_encryption_key_stops_the_server_instead_of_storing_weakly()
    {
        Assert.Throws<InvalidOperationException>(() => Protector("not base64!"));
        Assert.Throws<InvalidOperationException>(() => Protector(Convert.ToBase64String(new byte[16])));
    }

    [Fact]
    public void Storage_keys_cannot_point_outside_the_folder()
    {
        var store = new LocalPrivateDocumentStore(new FakeEnvironment(), Options.Create(new DriverVerificationOptions { StoragePath = "store" }));
        Assert.Null(store.Open("../secret.png", "image/png"));
        Assert.Null(store.Open("..\\..\\x.jpg", "image/jpeg"));
        Assert.Null(store.Open("0123456789abcdef0123456789abcdef.exe", "image/png"));
        store.Delete("../../appsettings.json"); // refused silently, never deleted
    }

    [Fact]
    public async Task Stored_documents_get_random_names_and_can_be_read_back()
    {
        var env = new FakeEnvironment();
        var store = new LocalPrivateDocumentStore(env, Options.Create(new DriverVerificationOptions { StoragePath = "store" }));
        var key = await store.SaveAsync(new byte[] { 1, 2, 3 }, ".png");
        Assert.Matches("^[0-9a-f]{32}\\.png$", key);
        var opened = store.Open(key, "image/png");
        Assert.NotNull(opened);
        Assert.Equal("image/png", opened.ContentType);
        await opened.Content.DisposeAsync();
        store.Delete(key);
        Assert.Null(store.Open(key, "image/png"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new byte[] { 1 }, ".html"));
        Directory.Delete(env.ContentRootPath, true);
    }

    private sealed class FakeEnvironment : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "sc-driver-tests-" + Guid.NewGuid().ToString("N"));
        public string WebRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ApplicationName { get; set; } = "tests";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string EnvironmentName { get; set; } = "Testing";
    }

    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1")
        .UseSnakeCaseNamingConvention().Options);

    public static TheoryData<string> Queries => new() { "list", "search", "stats", "details", "document", "assignments", "waiting" };

    // Every admin query translates to SQL: it only fails because there is no database to run it on.
    [Theory]
    [MemberData(nameof(Queries))]
    public async Task Admin_queries_are_valid_SQL(string name)
    {
        await using var db = Db();
        var service = new DriverVerificationService(db, null!, new NotificationService(db));
        Task run = name switch
        {
            "list" => service.ListAsync(new DriverApplicationFilter(Status: "PENDING", From: new DateOnly(2026, 1, 1), To: new DateOnly(2026, 12, 1), Oldest: true, Page: 2)),
            "search" => service.ListAsync(new DriverApplicationFilter(Search: "karim_%")),
            "stats" => service.StatsAsync(),
            "details" => service.DetailsAsync(Guid.NewGuid()),
            "document" => service.DocumentAsync(Guid.NewGuid(), "nid_front"),
            "assignments" => service.AssignmentsAsync(Guid.NewGuid()),
            _ => service.WaitingCountAsync(),
        };
        var error = await Record.ExceptionAsync(() => run);
        Assert.NotNull(error);
        Assert.True(error is not InvalidOperationException || error.Message.Contains("transient failure"), $"{name}: {error.Message} | {error.InnerException?.Message}");
    }
}

// F21 end to end against PostgreSQL (skipped without SMARTCOMMUTE_TEST_DB).
[Collection(ServerCollection.Name)]
public class DriverRegistrationFlowTests(TestServerFixture server)
{
    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    private const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    // Registration is rate limited per address, so each applicant comes from their own.
    private HttpClient Visitor(string? token = null)
    {
        var client = server.ClientFor(token);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.{Random.Shared.Next(256)}.{Random.Shared.Next(256)}.{Random.Shared.Next(1, 255)}");
        return client;
    }

    private static string Digits(int count) => string.Concat(Enumerable.Range(0, count).Select(_ => Random.Shared.Next(10)));

    private static Dictionary<string, object?> Application(Action<Dictionary<string, object?>>? change = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["fullName"] = "Karim Uddin", ["email"] = $"{Guid.NewGuid():N}@driver.test", ["phone"] = "01712345678",
            ["password"] = "Drive2026", ["confirmPassword"] = "Drive2026", ["dateOfBirth"] = "1990-05-01", ["gender"] = "Male",
            ["licenseNumber"] = $"DK{Digits(10)}", ["licenseType"] = "Professional", ["licenseExpiryDate"] = "2031-01-01", ["drivingExperienceYears"] = 8,
            ["nidNumber"] = Digits(10), ["busName"] = "Turag Paribahan", ["operatorName"] = "Turag Transport", ["route"] = "Gazipur – Jatrabari",
            ["employmentType"] = "Full-time", ["presentAddress"] = "House 12, Road 3, Uttara", ["district"] = "Dhaka", ["area"] = "Uttara",
            ["confirmAccuracy"] = true, ["authorizeVerification"] = true, ["acceptTerms"] = true,
            ["documents"] = new Dictionary<string, string> { ["license_front"] = Png, ["nid_front"] = Png, ["license_back"] = Png },
            // Things a client might try to set for itself; the server must ignore them.
            ["role"] = "admin", ["verificationStatus"] = "APPROVED", ["userId"] = Guid.NewGuid(),
        };
        change?.Invoke(body);
        return body;
    }

    private async Task<(Guid DriverId, HttpClient Client, JsonElement Body)> RegisterAsync(Action<Dictionary<string, object?>>? change = null)
    {
        var response = await Visitor().PostAsJsonAsync("/api/driver-registration/register", Application(change));
        var json = await JsonOf(response);
        Assert.True(response.StatusCode == HttpStatusCode.Created, json.ToString());
        var token = json.GetProperty("token").GetString()!;
        var client = server.ClientFor(token);
        return (Guid.Parse(json.GetProperty("user").GetProperty("_id").GetString()!), client, json);
    }

    [SkippableFact]
    public async Task Valid_registration_creates_a_pending_driver_and_keeps_numbers_private()
    {
        RequireServer();
        var nid = Digits(17);
        var (driverId, client, body) = await RegisterAsync(b => b["nidNumber"] = nid);
        Assert.Equal("driver", body.GetProperty("user").GetProperty("role").GetString());
        Assert.Equal("PENDING", body.GetProperty("data").GetProperty("verificationStatus").GetString());
        Assert.Equal("PENDING_VERIFICATION", body.GetProperty("data").GetProperty("accountStatus").GetString());

        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profile = await db.DriverProfiles.Include(p => p.Documents).Include(p => p.History).AsNoTracking().SingleAsync(p => p.UserId == driverId);
        Assert.DoesNotContain(nid, profile.NidNumberEncrypted);
        Assert.Equal(nid, scope.ServiceProvider.GetRequiredService<SensitiveDataProtector>().Decrypt(profile.NidNumberEncrypted));
        Assert.Equal(3, profile.Documents.Count);
        Assert.All(profile.Documents, d => Assert.Matches("^[0-9a-f]{32}\\.png$", d.PrivateStorageKey));
        Assert.Single(profile.History);
        Assert.Equal(UserRoles.Driver, (await db.Users.AsNoTracking().SingleAsync(u => u.Id == driverId)).Role);

        // The driver's own views never include the full numbers or document links.
        var mine = await (await client.GetAsync("/api/driver-registration/my-profile")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(nid, mine);
        Assert.Contains($"••••••{nid[^4..]}", mine);
        Assert.DoesNotContain("/documents/", mine);
    }

    [SkippableFact]
    public async Task Bad_applications_are_refused()
    {
        RequireServer();
        var (_, _, first) = await RegisterAsync();
        var email = first.GetProperty("user").GetProperty("email").GetString();

        async Task<HttpStatusCode> Try(Action<Dictionary<string, object?>> change) =>
            (await Visitor().PostAsJsonAsync("/api/driver-registration/register", Application(change))).StatusCode;

        Assert.Equal(HttpStatusCode.Conflict, await Try(b => b["email"] = email));
        Assert.Equal(HttpStatusCode.BadRequest, await Try(b => b["email"] = "not-an-email"));
        Assert.Equal(HttpStatusCode.BadRequest, await Try(b => b["dateOfBirth"] = DateTime.UtcNow.AddYears(-17).ToString("yyyy-MM-dd")));
        Assert.Equal(HttpStatusCode.BadRequest, await Try(b => b["licenseExpiryDate"] = "2020-01-01"));
        Assert.Equal(HttpStatusCode.BadRequest, await Try(b => b["drivingExperienceYears"] = -2));
        Assert.Equal(HttpStatusCode.BadRequest, await Try(b => b["documents"] = new Dictionary<string, string> { ["license_front"] = Png }));
        Assert.Equal(HttpStatusCode.BadRequest, await Try(b => b["documents"] = new Dictionary<string, string> { ["license_front"] = "data:image/gif;base64,R0lGODlhAQABAAAAACw=", ["nid_front"] = Png }));
        Assert.Equal(HttpStatusCode.BadRequest, await Try(b => b["acceptTerms"] = false));
    }

    [SkippableFact]
    public async Task The_same_licence_or_NID_cannot_register_twice()
    {
        RequireServer();
        var license = $"DK{Digits(10)}";
        var nid = Digits(13);
        await RegisterAsync(b => { b["licenseNumber"] = license; b["nidNumber"] = nid; });
        var again = await Visitor().PostAsJsonAsync("/api/driver-registration/register", Application(b => b["licenseNumber"] = license.ToLowerInvariant()));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        var message = (await JsonOf(again)).GetProperty("message").GetString()!;
        Assert.DoesNotContain(license, message); // nothing about the other application is revealed
        Assert.Equal(HttpStatusCode.Conflict, (await Visitor().PostAsJsonAsync("/api/driver-registration/register", Application(b => b["nidNumber"] = nid))).StatusCode);
    }

    [SkippableFact]
    public async Task Admin_reviews_and_every_decision_is_recorded()
    {
        RequireServer();
        var (admin, adminClient) = await server.CreateAdminAsync();
        var (driverId, driverClient, _) = await RegisterAsync();

        // The new application notified the admin.
        await using (var scope = server.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.Notifications.AnyAsync(n => n.UserId == admin.Id && n.SenderId == driverId));
        }

        var list = await JsonOf(await adminClient.GetAsync("/api/admin/drivers?status=PENDING&search=Karim"));
        Assert.Contains(list.GetProperty("data").EnumerateArray(), row => row.GetProperty("driverId").GetString() == driverId.ToString());
        Assert.StartsWith("••••••", list.GetProperty("data")[0].GetProperty("licenseNumberMasked").GetString());

        var details = await JsonOf(await adminClient.GetAsync($"/api/admin/drivers/{driverId}"));
        var docUrl = details.GetProperty("data").GetProperty("documents")[0].GetProperty("url").GetString()!;
        var doc = await adminClient.GetAsync(docUrl);
        Assert.Equal(HttpStatusCode.OK, doc.StatusCode);
        Assert.Equal("image/png", doc.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", doc.Headers.CacheControl?.ToString());

        Assert.Equal(HttpStatusCode.OK, (await adminClient.PutAsJsonAsync($"/api/admin/drivers/{driverId}/start-review", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await adminClient.PutAsJsonAsync($"/api/admin/drivers/{driverId}/request-resubmission", new { reason = "Blurry", fields = Array.Empty<string>() })).StatusCode);
        var resub = await adminClient.PutAsJsonAsync($"/api/admin/drivers/{driverId}/request-resubmission", new { reason = "The NID photo is blurry.", fields = new[] { "nid_front" } });
        Assert.Equal(HttpStatusCode.OK, resub.StatusCode);

        var status = await JsonOf(await driverClient.GetAsync("/api/driver-registration/status"));
        Assert.Equal("RESUBMISSION_REQUIRED", status.GetProperty("data").GetProperty("verificationStatus").GetString());
        Assert.Equal("nid_front", status.GetProperty("data").GetProperty("requestedFields")[0].GetString());

        // Resubmitting without the requested document fails; with it, back to pending.
        Assert.Equal(HttpStatusCode.BadRequest, (await driverClient.PostAsJsonAsync("/api/driver-registration/resubmit", new { })).StatusCode);
        var resubmitted = await driverClient.PostAsJsonAsync("/api/driver-registration/resubmit",
            new { documents = new Dictionary<string, string> { ["nid_front"] = Png }, fullName = "Someone Else" });
        Assert.Equal(HttpStatusCode.OK, resubmitted.StatusCode);
        var after = (await JsonOf(resubmitted)).GetProperty("data");
        Assert.Equal("PENDING", after.GetProperty("verificationStatus").GetString());
        Assert.Equal("Karim Uddin", after.GetProperty("fullName").GetString()); // not requested, so not changed

        Assert.Equal(HttpStatusCode.BadRequest, (await adminClient.PutAsJsonAsync($"/api/admin/drivers/{driverId}/reject", new { reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await adminClient.PutAsJsonAsync($"/api/admin/drivers/{driverId}/approve", new { reason = "Documents match." })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await adminClient.PutAsJsonAsync($"/api/admin/drivers/{driverId}/reject", new { reason = "Changed my mind" })).StatusCode);

        await using (var scope = server.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var history = await db.DriverVerificationHistory.AsNoTracking().Where(h => h.DriverProfile!.UserId == driverId).OrderBy(h => h.CreatedAt).ToListAsync();
            Assert.Equal(["PENDING", "UNDER_REVIEW", "RESUBMISSION_REQUIRED", "PENDING", "APPROVED"], history.Select(h => h.NewStatus));
            var approval = history[^1];
            Assert.Equal(admin.Id, approval.AdminId);
            Assert.Equal("PENDING", approval.PreviousStatus);
            Assert.Equal("Documents match.", approval.Reason);
            Assert.True(await db.Notifications.AnyAsync(n => n.UserId == driverId && n.Title == "Driver account verified"));
        }
    }

    [SkippableFact]
    public async Task Rejection_needs_a_reason_and_is_shown_to_the_driver()
    {
        RequireServer();
        var (_, adminClient) = await server.CreateAdminAsync();
        var (driverId, driverClient, _) = await RegisterAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await adminClient.PutAsJsonAsync($"/api/admin/drivers/{driverId}/reject", new { reason = "no" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await adminClient.PutAsJsonAsync($"/api/admin/drivers/{driverId}/reject", new { reason = "Licence does not match the NID name." })).StatusCode);
        var status = (await JsonOf(await driverClient.GetAsync("/api/driver-registration/status"))).GetProperty("data");
        Assert.Equal("REJECTED", status.GetProperty("verificationStatus").GetString());
        Assert.Equal("REJECTED", status.GetProperty("accountStatus").GetString());
        Assert.Equal("Licence does not match the NID name.", status.GetProperty("rejectionReason").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await driverClient.GetAsync("/api/driver/dashboard")).StatusCode);
    }

    [SkippableFact]
    public async Task Access_follows_role_verification_and_bus_assignment()
    {
        RequireServer();
        var (_, commuter) = await server.CreateUserAsync();
        var (_, adminClient) = await server.CreateAdminAsync();
        var (driverId, driver, _) = await RegisterAsync();
        var anonymous = server.ClientFor(null);

        // Commuters and visitors cannot use driver or admin endpoints.
        Assert.Equal(HttpStatusCode.Forbidden, (await commuter.GetAsync("/api/driver-registration/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await commuter.GetAsync("/api/driver/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await commuter.GetAsync("/api/admin/drivers")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/driver-registration/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/admin/drivers/{driverId}/documents/nid_front")).StatusCode);

        // A driver cannot use admin endpoints, decide on their own application or see documents.
        Assert.Equal(HttpStatusCode.Forbidden, (await driver.GetAsync("/api/admin/drivers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await driver.PutAsJsonAsync($"/api/admin/drivers/{driverId}/approve", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await driver.GetAsync($"/api/admin/drivers/{driverId}/documents/nid_front")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await commuter.GetAsync($"/api/admin/drivers/{driverId}/documents/nid_front")).StatusCode);
        // Resubmitting while pending is refused: it is not a way to change the status.
        Assert.Equal(HttpStatusCode.Conflict, (await driver.PostAsJsonAsync("/api/driver-registration/resubmit", new { verificationStatus = "APPROVED" })).StatusCode);

        // Pending: no dashboard. Approved: dashboard, but no bus operations until an admin assigns a bus.
        Assert.Equal(HttpStatusCode.Forbidden, (await driver.GetAsync("/api/driver/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await adminClient.PutAsJsonAsync($"/api/admin/drivers/{driverId}/approve", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await driver.GetAsync("/api/driver/dashboard")).StatusCode);
        // F22: the assigned-bus view is open to approved drivers and is simply empty until a bus is assigned.
        Assert.Empty((await JsonOf(await driver.GetAsync("/api/driver/assigned-bus"))).GetProperty("data").EnumerateArray());

        Guid busId;
        await using (var scope = server.Scope())
        {
            busId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Buses.OrderBy(b => b.RouteNumber).Select(b => b.Id).FirstAsync();
        }
        var assigned = await adminClient.PutAsJsonAsync($"/api/admin/drivers/{driverId}/assign-bus", new { busId });
        Assert.Equal(HttpStatusCode.Created, assigned.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await adminClient.PutAsJsonAsync($"/api/admin/drivers/{driverId}/assign-bus", new { busId })).StatusCode);
        var buses = await JsonOf(await driver.GetAsync("/api/driver/assigned-bus"));
        Assert.Equal(busId.ToString(), buses.GetProperty("data")[0].GetProperty("id").GetString());
    }

    [SkippableFact]
    public async Task Commuter_signup_still_works_but_cannot_create_a_driver()
    {
        RequireServer();
        var email = $"{Guid.NewGuid():N}@commuter.test";
        var signup = await Visitor().PostAsJsonAsync("/auth/signup", new { name = "New Rider", email, password = "Secret123" });
        Assert.Equal(HttpStatusCode.OK, signup.StatusCode);
        Assert.Equal("commuter", (await JsonOf(signup)).GetProperty("user").GetProperty("role").GetString());
        var login = await Visitor().PostAsJsonAsync("/auth/login", new { email, password = "Secret123", role = "commuter" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await JsonOf(login)).GetProperty("token").GetString();
        var me = Visitor(token);
        Assert.Equal(HttpStatusCode.OK, (await me.GetAsync("/auth/validate")).StatusCode);

        // The old way of making a driver account without documents is closed.
        var driver = await Visitor().PostAsJsonAsync("/auth/signup", new { name = "Sneaky", email = $"{Guid.NewGuid():N}@x.test", password = "Secret123", role = "driver" });
        Assert.Equal(HttpStatusCode.BadRequest, driver.StatusCode);
    }
}
