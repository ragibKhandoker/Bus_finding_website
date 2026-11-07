using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Tests;

[Collection(ServerCollection.Name)]
public class ReviewTests(TestServerFixture server)
{
    private void RequireServer() => Skip.If(server.Factory is null, server.SkipReason);

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    // A bus of its own for each test, so averages are not shared between tests.
    private async Task<Guid> NewBus()
    {
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bus = new Bus { BusName = "Test Paribahan", RouteNumber = $"T-{Guid.NewGuid():N}"[..20] };
        db.Buses.Add(bus);
        await db.SaveChangesAsync();
        return bus.Id;
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, Guid busId, object rating, string comment = "Clean and on time.") =>
        client.PostAsJsonAsync("/api/reviews", new { busId, rating, comment });

    private static async Task<string> ReviewId(HttpClient client, Guid busId, int rating, string comment = "Clean and on time.")
    {
        var response = await Post(client, busId, rating, comment);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonOf(response)).GetProperty("review").GetProperty("_id").GetString()!;
    }

    private async Task<JsonElement> Summary(Guid busId) =>
        (await JsonOf(await server.ClientFor(null).GetAsync($"/api/ratings/bus/{busId}"))).GetProperty("data");

    private static Task<HttpResponseMessage> React(HttpClient client, string reviewId, string reaction) =>
        client.PostAsync($"/api/reviews/{reviewId}/{reaction}", null);

    // ---- ratings ----

    [SkippableFact]
    public async Task Ratings_of_one_to_five_are_accepted_and_others_refused()
    {
        RequireServer();
        var busId = await NewBus();
        var (user, client) = await server.CreateUserAsync();

        Assert.Equal(HttpStatusCode.Created, (await Post(client, busId, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Post(client, busId, 5)).StatusCode);
        foreach (var bad in new object[] { 0, 6, -1, "five" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, busId, bad)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/reviews", new { busId, comment = "No stars" })).StatusCode);
        var fraction = await client.PostAsync("/api/reviews", new StringContent($$"""{"busId":"{{busId}}","rating":4.5,"comment":"Half"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, fraction.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, busId, 4, "   ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, busId, 4, new string('a', 1001))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(client, Guid.NewGuid(), 4)).StatusCode);

        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // The database refuses an out-of-range rating even when the API is bypassed.
        db.Reviews.Add(new Review { BusId = busId, UserId = user.Id, Rating = 9, Comment = "direct" });
        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("ck_reviews_rating_range", refused.InnerException?.Message);
    }

    [SkippableFact]
    public async Task Average_follows_creating_editing_and_deleting()
    {
        RequireServer();
        var busId = await NewBus();
        var (_, a) = await server.CreateUserAsync();
        var (_, b) = await server.CreateUserAsync();

        var empty = await Summary(busId);
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("averageRating").ValueKind);
        Assert.Equal(0, empty.GetProperty("totalRatings").GetInt32());

        var first = await ReviewId(a, busId, 5);
        await ReviewId(b, busId, 2);
        await ReviewId(b, busId, 4);
        var summary = await Summary(busId);
        Assert.Equal(3.7, summary.GetProperty("averageRating").GetDouble());
        Assert.Equal(3, summary.GetProperty("totalRatings").GetInt32());
        Assert.Equal(1, summary.GetProperty("distribution").GetProperty("5").GetInt32());
        Assert.Equal(0, summary.GetProperty("distribution").GetProperty("3").GetInt32());

        var edit = await a.PutAsJsonAsync($"/api/reviews/{first}", new { rating = 2 });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal(2.7, (await JsonOf(edit)).GetProperty("summary").GetProperty("averageRating").GetDouble());

        var delete = await a.DeleteAsync($"/api/reviews/{first}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.Equal(3.0, (await JsonOf(delete)).GetProperty("summary").GetProperty("averageRating").GetDouble());
        Assert.Equal(2, (await Summary(busId)).GetProperty("totalRatings").GetInt32());
    }

    [SkippableFact]
    public async Task Replies_and_reactions_never_count_as_ratings()
    {
        RequireServer();
        var busId = await NewBus();
        var (_, a) = await server.CreateUserAsync();
        var (_, b) = await server.CreateUserAsync();
        var reviewId = await ReviewId(a, busId, 4);
        await b.PostAsJsonAsync($"/api/reviews/{reviewId}/reply", new { comment = "Agreed!" });
        await React(b, reviewId, "like");
        await React(a, reviewId, "like");

        var summary = await Summary(busId);
        Assert.Equal(4.0, summary.GetProperty("averageRating").GetDouble());
        Assert.Equal(1, summary.GetProperty("totalRatings").GetInt32());
    }

    // ---- ownership ----

    [SkippableFact]
    public async Task Only_the_author_can_edit_and_delete()
    {
        RequireServer();
        var busId = await NewBus();
        var (author, authorClient) = await server.CreateUserAsync();
        var (_, otherClient) = await server.CreateUserAsync();
        var response = await authorClient.PostAsJsonAsync("/api/reviews", new { busId, rating = 3, comment = "Okay ride", userId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var review = (await JsonOf(response)).GetProperty("review");
        var reviewId = review.GetProperty("_id").GetString();
        Assert.Equal(author.Id.ToString(), review.GetProperty("userId").GetString());
        Assert.False(review.GetProperty("author").TryGetProperty("email", out _));

        Assert.Equal(HttpStatusCode.Forbidden, (await otherClient.PutAsJsonAsync($"/api/reviews/{reviewId}", new { rating = 1, comment = "Hacked" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherClient.DeleteAsync($"/api/reviews/{reviewId}")).StatusCode);

        var edit = await authorClient.PutAsJsonAsync($"/api/reviews/{reviewId}", new { rating = 4, comment = "  Better than I said  " });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var edited = (await JsonOf(edit)).GetProperty("review");
        Assert.Equal(4, edited.GetProperty("rating").GetInt32());
        Assert.Equal("Better than I said", edited.GetProperty("comment").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await authorClient.PutAsJsonAsync($"/api/reviews/{reviewId}", new { rating = 7 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await authorClient.PutAsJsonAsync($"/api/reviews/{reviewId}", new { comment = " " })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await authorClient.DeleteAsync($"/api/reviews/{reviewId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await authorClient.DeleteAsync($"/api/reviews/{reviewId}")).StatusCode);
    }

    [SkippableFact]
    public async Task Guests_and_bad_tokens_can_read_but_not_write()
    {
        RequireServer();
        var busId = await NewBus();
        var (_, client) = await server.CreateUserAsync();
        var reviewId = await ReviewId(client, busId, 5);

        foreach (var guest in new[] { server.ClientFor(null), server.ClientFor("not.a.token") })
        {
            Assert.Equal(HttpStatusCode.OK, (await server.ClientFor(null).GetAsync($"/api/reviews/bus/{busId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Post(guest, busId, 5)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PutAsJsonAsync($"/api/reviews/{reviewId}", new { rating = 1 })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await guest.DeleteAsync($"/api/reviews/{reviewId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PostAsJsonAsync($"/api/reviews/{reviewId}/reply", new { comment = "hi" })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await React(guest, reviewId, "like")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PostAsJsonAsync("/api/ratings", new { busId, rating = 5 })).StatusCode);
        }
    }

    // ---- reactions ----

    [SkippableFact]
    public async Task Reactions_toggle_and_switch_without_double_counting()
    {
        RequireServer();
        var busId = await NewBus();
        var (_, author) = await server.CreateUserAsync();
        var (reader, readerClient) = await server.CreateUserAsync();
        var reviewId = await ReviewId(author, busId, 4);

        async Task Expect(HttpResponseMessage r, int likes, int dislikes, string? mine)
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var body = await JsonOf(r);
            Assert.Equal(likes, body.GetProperty("likes").GetInt32());
            Assert.Equal(dislikes, body.GetProperty("dislikes").GetInt32());
            Assert.Equal(mine, body.GetProperty("myReaction").ValueKind == JsonValueKind.Null ? null : body.GetProperty("myReaction").GetString());
        }

        await Expect(await React(readerClient, reviewId, "like"), 1, 0, "like");
        await Expect(await React(readerClient, reviewId, "like"), 0, 0, null);
        await Expect(await React(readerClient, reviewId, "dislike"), 0, 1, "dislike");
        await Expect(await React(readerClient, reviewId, "dislike"), 0, 0, null);
        await Expect(await React(readerClient, reviewId, "like"), 1, 0, "like");
        await Expect(await React(readerClient, reviewId, "dislike"), 0, 1, "dislike");
        await Expect(await React(author, reviewId, "like"), 1, 1, "like");

        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.ReviewReactions.CountAsync(x => x.ReviewId == Guid.Parse(reviewId) && x.UserId == reader.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await React(readerClient, Guid.NewGuid().ToString(), "like")).StatusCode);

        // After a refresh the reader's own reaction comes back with the review.
        var list = await JsonOf(await readerClient.GetAsync($"/api/reviews/bus/{busId}"));
        var review = Assert.Single(list.GetProperty("reviews").EnumerateArray());
        Assert.Equal("dislike", review.GetProperty("myReaction").GetString());
        Assert.Equal(1, review.GetProperty("likes").GetInt32());
    }

    [SkippableFact]
    public async Task Simultaneous_clicks_leave_one_reaction()
    {
        RequireServer();
        var busId = await NewBus();
        var (_, author) = await server.CreateUserAsync();
        var (reader, readerClient) = await server.CreateUserAsync();
        var reviewId = await ReviewId(author, busId, 4);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => React(readerClient, reviewId, i % 2 == 0 ? "like" : "dislike")));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.ReviewReactions.CountAsync(x => x.ReviewId == Guid.Parse(reviewId) && x.UserId == reader.Id) <= 1);
    }

    // ---- replies ----

    [SkippableFact]
    public async Task Replies_appear_under_their_review()
    {
        RequireServer();
        var busId = await NewBus();
        var otherBus = await NewBus();
        var (_, a) = await server.CreateUserAsync();
        var (replier, b) = await server.CreateUserAsync();
        var first = await ReviewId(a, busId, 5, "First review");
        var second = await ReviewId(a, busId, 3, "Second review");

        var reply = await b.PostAsJsonAsync($"/api/reviews/{first}/reply", new { busId, comment = "  Same here  " });
        Assert.Equal(HttpStatusCode.Created, reply.StatusCode);
        var created = (await JsonOf(reply)).GetProperty("reply");
        Assert.Equal("Same here", created.GetProperty("comment").GetString());
        Assert.Equal(replier.Id.ToString(), created.GetProperty("author").GetProperty("id").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsJsonAsync($"/api/reviews/{Guid.NewGuid()}/reply", new { comment = "hi" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await b.PostAsJsonAsync($"/api/reviews/{first}/reply", new { comment = "   " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await b.PostAsJsonAsync($"/api/reviews/{first}/reply", new { busId = otherBus, comment = "wrong bus" })).StatusCode);

        var list = (await JsonOf(await server.ClientFor(null).GetAsync($"/api/reviews/bus/{busId}"))).GetProperty("reviews").EnumerateArray().ToList();
        Assert.Equal(2, list.Count);
        var withReply = list.Single(r => r.GetProperty("_id").GetString() == first);
        Assert.Equal(["Same here"], withReply.GetProperty("repliesList").EnumerateArray().Select(r => r.GetProperty("comment").GetString()));
        Assert.Empty(list.Single(r => r.GetProperty("_id").GetString() == second).GetProperty("repliesList").EnumerateArray());

        // A reply's author can delete it; nobody else can.
        var replyId = created.GetProperty("_id").GetString();
        Assert.Equal(HttpStatusCode.Forbidden, (await a.DeleteAsync($"/api/reviews/replies/{replyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.DeleteAsync($"/api/reviews/replies/{replyId}")).StatusCode);
    }

    [SkippableFact]
    public async Task Deleting_a_review_removes_its_replies_and_reactions()
    {
        RequireServer();
        var busId = await NewBus();
        var (_, a) = await server.CreateUserAsync();
        var (_, b) = await server.CreateUserAsync();
        var reviewId = await ReviewId(a, busId, 5);
        await b.PostAsJsonAsync($"/api/reviews/{reviewId}/reply", new { comment = "Nice" });
        await React(b, reviewId, "like");

        Assert.Equal(HttpStatusCode.OK, (await a.DeleteAsync($"/api/reviews/{reviewId}")).StatusCode);
        await using var scope = server.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.Parse(reviewId);
        Assert.False(await db.ReviewReplies.AnyAsync(r => r.ReviewId == id));
        Assert.False(await db.ReviewReactions.AnyAsync(r => r.ReviewId == id));
    }
}
