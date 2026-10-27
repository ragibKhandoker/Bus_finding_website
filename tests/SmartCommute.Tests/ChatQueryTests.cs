using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;

namespace SmartCommute.Tests;

// Checks that the chat queries translate to PostgreSQL SQL. ToQueryString never opens a connection, so these run
// without a database and catch a query EF Core cannot translate before it reaches a server.
public class ChatQueryTests
{
    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
        .UseSnakeCaseNamingConvention()
        .Options);

    [Fact]
    public void Message_cursor_compares_created_at_and_id_together()
    {
        using var db = Db();
        var cursor = new { CreatedAt = DateTime.UtcNow, Id = Guid.NewGuid() };
        var older = db.ChatMessages
            .Where(m => EF.Functions.LessThan(ValueTuple.Create(m.CreatedAt, m.Id), ValueTuple.Create(cursor.CreatedAt, cursor.Id)))
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .ToQueryString();
        Assert.Contains("(c.created_at, c.id) <", older);
        Assert.Contains("ORDER BY c.created_at DESC, c.id DESC", older);
    }

    [Fact]
    public void Unread_counts_skip_the_readers_own_messages()
    {
        using var db = Db();
        var service = new ChatService(db, new FriendService(db, new NotificationService(db)));
        var sql = service.Unread(Guid.NewGuid())
            .GroupBy(m => new { m.ChatId, m.SenderId })
            .Select(g => new ChatUnread(g.Key.ChatId, g.Key.SenderId, g.Count()))
            .ToQueryString();
        Assert.Contains("NOT (c.read)", sql);
        Assert.Contains("c.sender_id <> @", sql);
        Assert.Contains("GROUP BY", sql);
    }

    [Fact]
    public void Chat_pairs_use_the_same_order_as_friendships()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        Assert.Equal(Friendship.Order(a, b), Friendship.Order(b, a));
    }
}
