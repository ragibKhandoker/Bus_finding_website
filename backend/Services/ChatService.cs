using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

// The outcome of a chat action: an HTTP status code and message, plus whatever the action produced.
public record ChatResult<T>(int StatusCode, string Message, T? Value = default)
{
    public bool Succeeded => StatusCode is >= 200 and < 300;
}

public record MessagePage(List<ChatMessage> Messages, bool HasMore);

public record ChatUnread(Guid ChatId, Guid OtherUserId, int Count);

// One-to-one conversations between friends. Every method takes the signed-in user's id from the token and only
// touches conversations that user belongs to. A conversation someone is not part of is reported as not found,
// so guessing ids reveals nothing.
public class ChatService(AppDbContext db, FriendService friends)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;

    public static ChatResult<T> NotFound<T>() => new(StatusCodes.Status404NotFound, "Chat not found.");

    public Task<Chat?> FindAsync(Guid chatId, Guid me) => db.Chats
        .Include(c => c.User1)
        .Include(c => c.User2)
        .AsNoTracking()
        .FirstOrDefaultAsync(c => c.Id == chatId && (c.User1Id == me || c.User2Id == me));

    // Returns the pair's conversation, creating it the first time. The insert skips a pair that already exists,
    // so two simultaneous first opens end up with the same single row. Value.Created says whether this call made it.
    public async Task<ChatResult<(Chat Chat, bool Created)>> OpenAsync(Guid me, Guid otherUserId)
    {
        if (otherUserId == me)
        {
            return new(StatusCodes.Status400BadRequest, "You cannot chat with yourself.");
        }
        if (!await db.Users.AnyAsync(u => u.Id == otherUserId))
        {
            return new(StatusCodes.Status404NotFound, "User not found.");
        }
        if (!await friends.AreFriendsAsync(me, otherUserId))
        {
            return new(StatusCodes.Status403Forbidden, "You can only chat with your friends.");
        }

        var (user1, user2) = Friendship.Order(me, otherUserId);
        var created = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO chats (id, user1id, user2id, created_at)
            VALUES ({Guid.NewGuid()}, {user1}, {user2}, {DateTime.UtcNow})
            ON CONFLICT (user1id, user2id) DO NOTHING
            """) == 1;
        var chat = await db.Chats
            .Include(c => c.User1)
            .Include(c => c.User2)
            .AsNoTracking()
            .SingleAsync(c => c.User1Id == user1 && c.User2Id == user2);
        return new(created ? StatusCodes.Status201Created : StatusCodes.Status200OK, created ? "Chat started." : "", (chat, created));
    }

    // The signed-in user's conversations, most recent activity first.
    public Task<List<Chat>> ListAsync(Guid me) => db.Chats
        .Include(c => c.User1)
        .Include(c => c.User2)
        .Where(c => c.User1Id == me || c.User2Id == me)
        .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
        .AsNoTracking()
        .ToListAsync();

    public Task<Dictionary<Guid, ChatMessage>> LastMessagesAsync(List<Guid> chatIds) => db.ChatMessages
        .Where(m => chatIds.Contains(m.ChatId))
        .GroupBy(m => m.ChatId)
        .Select(g => g.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).First())
        .ToDictionaryAsync(m => m.ChatId);

    // Messages the user has not read yet, per conversation. Their own messages never count.
    public IQueryable<ChatMessage> Unread(Guid me) => db.ChatMessages
        .Where(m => !m.Read && m.SenderId != me && (m.Chat!.User1Id == me || m.Chat.User2Id == me));

    public Task<List<ChatUnread>> UnreadCountsAsync(Guid me) => Unread(me)
        .GroupBy(m => new { m.ChatId, m.SenderId })
        .Select(g => new ChatUnread(g.Key.ChatId, g.Key.SenderId, g.Count()))
        .ToListAsync();

    // One page of a conversation, oldest first. Without a cursor it is the latest messages; with before= it is the
    // messages just older than that one; with after= the messages newer than it (used to pick up new arrivals).
    // Messages are ordered by (created_at, id), so a cursor never skips or repeats a message.
    public async Task<ChatResult<MessagePage>> MessagesAsync(Guid me, Guid chatId, Guid? before, Guid? after, int limit)
    {
        if (await FindAsync(chatId, me) is null)
        {
            return NotFound<MessagePage>();
        }
        if (before is not null && after is not null)
        {
            return new(StatusCodes.Status400BadRequest, "Use either before or after, not both.");
        }
        limit = Math.Clamp(limit, 1, MaxPageSize);

        var query = db.ChatMessages.Include(m => m.Sender).Where(m => m.ChatId == chatId).AsNoTracking();
        var cursorId = before ?? after;
        if (cursorId is not null)
        {
            var cursor = await db.ChatMessages.AsNoTracking()
                .Where(m => m.Id == cursorId && m.ChatId == chatId)
                .Select(m => new { m.CreatedAt, m.Id })
                .FirstOrDefaultAsync();
            if (cursor is null)
            {
                return new(StatusCodes.Status400BadRequest, "That message is not in this chat.");
            }
            query = before is not null
                ? query.Where(m => EF.Functions.LessThan(ValueTuple.Create(m.CreatedAt, m.Id), ValueTuple.Create(cursor.CreatedAt, cursor.Id)))
                : query.Where(m => EF.Functions.GreaterThan(ValueTuple.Create(m.CreatedAt, m.Id), ValueTuple.Create(cursor.CreatedAt, cursor.Id)));
        }

        List<ChatMessage> page;
        if (after is not null)
        {
            page = await query.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).Take(limit + 1).ToListAsync();
        }
        else
        {
            page = await query.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Take(limit + 1).ToListAsync();
            page.Reverse();
        }
        var hasMore = page.Count > limit;
        if (hasMore)
        {
            // The extra row only showed there is more; drop it from the far end of the page.
            page.RemoveAt(after is not null ? page.Count - 1 : 0);
        }
        return new(StatusCodes.Status200OK, "", new MessagePage(page, hasMore));
    }

    // Saves a message from the signed-in user. The text is trimmed first; blank text is refused. The sender must
    // still be friends with the other person. Saving the message and moving the chat's activity time happen in one
    // transaction, so a failed write is never reported as sent.
    public async Task<ChatResult<ChatMessage>> SendAsync(Guid me, Guid chatId, string? rawText)
    {
        var text = rawText?.Trim() ?? "";
        if (text.Length == 0)
        {
            return new(StatusCodes.Status400BadRequest, "Message cannot be empty.");
        }
        if (text.Length > ChatMessage.MaxLength)
        {
            return new(StatusCodes.Status400BadRequest, "A message can be at most 2000 characters.");
        }
        var chat = await FindAsync(chatId, me);
        if (chat is null)
        {
            return NotFound<ChatMessage>();
        }
        if (!await friends.AreFriendsAsync(me, chat.OtherParticipant(me)))
        {
            return new(StatusCodes.Status403Forbidden, "You can only message your friends.");
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var now = DateTime.UtcNow;
            var message = new ChatMessage { ChatId = chatId, SenderId = me, Text = text, CreatedAt = now };
            db.ChatMessages.Add(message);
            await db.SaveChangesAsync();
            await db.Chats
                .Where(c => c.Id == chatId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastMessageAt, c => c.LastMessageAt == null || c.LastMessageAt < now ? now : c.LastMessageAt));
            await transaction.CommitAsync();
            message.Sender = chat.User1Id == me ? chat.User1 : chat.User2;
            return new ChatResult<ChatMessage>(StatusCodes.Status201Created, "Message sent.", message);
        });
    }

    // Marks the other person's unread messages in this conversation as read, up to and including upTo when given
    // (the newest message the reader has on screen), so a message that arrives a moment later stays unread.
    // Running it again changes nothing. Value is how many messages it marked.
    public async Task<ChatResult<int>> MarkReadAsync(Guid me, Guid chatId, Guid? upTo)
    {
        if (await FindAsync(chatId, me) is null)
        {
            return NotFound<int>();
        }
        var query = db.ChatMessages.Where(m => m.ChatId == chatId && m.SenderId != me && !m.Read);
        if (upTo is not null)
        {
            var cursor = await db.ChatMessages.AsNoTracking()
                .Where(m => m.Id == upTo && m.ChatId == chatId)
                .Select(m => new { m.CreatedAt, m.Id })
                .FirstOrDefaultAsync();
            if (cursor is null)
            {
                return new(StatusCodes.Status400BadRequest, "That message is not in this chat.");
            }
            query = query.Where(m => EF.Functions.LessThanOrEqual(ValueTuple.Create(m.CreatedAt, m.Id), ValueTuple.Create(cursor.CreatedAt, cursor.Id)));
        }
        var now = DateTime.UtcNow;
        var marked = await query.ExecuteUpdateAsync(s => s.SetProperty(m => m.Read, true).SetProperty(m => m.ReadAt, now));
        return new(StatusCodes.Status200OK, "", marked);
    }
}
