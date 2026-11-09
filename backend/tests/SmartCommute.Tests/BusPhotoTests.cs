using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartCommute.Server.Data;
using SmartCommute.Server.Services;

namespace SmartCommute.Tests;

// The shared data-URL check used by verification images and bus photos; no database needed.
public class ImageDecodeTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, .. Enumerable.Repeat((byte)7, 200)];
    private static string DataUrl(string type, byte[] bytes) => $"data:{type};base64,{Convert.ToBase64String(bytes)}";

    [Fact]
    public void Accepts_a_real_jpeg_and_reports_its_type()
    {
        var image = ImageStorageService.Decode(DataUrl("image/jpg", Jpeg), 1024);
        Assert.Equal("image/jpeg", image.ContentType);
        Assert.Equal(".jpg", image.Extension);
        Assert.Equal(Jpeg, image.Bytes[..image.Length]);
    }

    [Theory]
    [InlineData("not a data url")]
    [InlineData("data:image/png;base64,/9j/4AAQ")] // declared PNG, bytes are JPEG
    [InlineData("data:image/svg+xml;base64,PHN2Zz4=")]
    [InlineData("data:image/jpeg,plain")]
    public void Refuses_anything_that_is_not_a_checked_image(string dataUrl) =>
        Assert.Throws<ArgumentException>(() => ImageStorageService.Decode(dataUrl, 1024));

    [Fact]
    public void Refuses_an_image_over_the_limit() =>
        Assert.Throws<ArgumentException>(() => ImageStorageService.Decode(DataUrl("image/jpeg", Jpeg), 100));
}

[Collection(ServerCollection.Name)]
public class BusPhotoTests(TestServerFixture server)
{
    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, .. Enumerable.Repeat((byte)9, 300)];
    private static string DataUrl(byte[] bytes) => $"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}";

    private async Task<Guid> AnyBusId()
    {
        using var scope = server.Factory!.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Buses.Select(b => b.Id).FirstAsync();
    }

    [SkippableFact]
    public async Task Only_admins_can_upload_and_everyone_can_see_the_photo()
    {
        RequireServer();
        var busId = await AnyBusId();
        var (_, user) = await server.CreateUserAsync();
        var refused = await user.PutAsJsonAsync($"/admin/buses/{busId}/photo", new { image = DataUrl(Jpeg) });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        var (_, admin) = await server.CreateAdminAsync();
        var saved = await admin.PutAsJsonAsync($"/admin/buses/{busId}/photo", new { image = DataUrl(Jpeg), credit = "  Test photographer " });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var anonymous = server.Factory!.CreateClient();
        var list = JsonDocument.Parse(await anonymous.GetStringAsync("/bus/photos")).RootElement.GetProperty("data");
        var entry = list.EnumerateArray().Single(p => p.GetProperty("busId").GetGuid() == busId);
        Assert.Equal("Test photographer", entry.GetProperty("credit").GetString());

        var photo = await anonymous.GetAsync(entry.GetProperty("url").GetString());
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        Assert.Equal("image/jpeg", photo.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Jpeg, await photo.Content.ReadAsByteArrayAsync());

        // Replacing keeps one photo per route; removing makes the route photo-less again.
        byte[] second = [.. Jpeg, 1, 2, 3];
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/admin/buses/{busId}/photo", new { image = DataUrl(second) })).StatusCode);
        Assert.Equal(second, await anonymous.GetByteArrayAsync($"/bus/{busId}/photo"));
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/admin/buses/{busId}/photo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/bus/{busId}/photo")).StatusCode);
    }

    [SkippableFact]
    public async Task A_fake_image_is_refused()
    {
        RequireServer();
        var (_, admin) = await server.CreateAdminAsync();
        var response = await admin.PutAsJsonAsync($"/admin/buses/{await AnyBusId()}/photo", new { image = "data:image/jpeg;base64,PHN2Zz4=" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
