using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;

namespace SmartCommute.Server.Services;

public record LostFoundThread(LostFoundConversation Conversation, int Unread, LostFoundMessage? Last);

// F17 messaging: a private conversation about one Lost & Found report, so the person who found an item can reach its
// owner (and the other way round) without sharing a phone number. Only the report's author and the one person who
// started the conversation can read or write in it; anyone else is told it does not exist. The other person is
// told about new messages with a notification.
public class LostFoundChatService(AppDbContext db, NotificationService notifications, LostFoundService? pictures = null)
{
    public const int PageSize = 100;

    private static string Clean(string? text) => (text ?? "").Trim();

    private static bool IsOwner(LostFoundConversation c, Guid me) => c.OwnerId == me;

    public static int UnreadFor(LostFoundConversation c, Guid me, IEnumerable<LostFoundMessage> messages)
    {
        var readAt = IsOwner(c, me) ? c.OwnerReadAt : c.OtherReadAt;
        return messages.Count(m => m.SenderId != me && (readAt is null || m.CreatedAt > readAt));
    }

    // The conversation, if this user is one of its two people.
    private Task<LostFoundConversation?> MineAsync(Guid me, Guid conversationId) => db.LostFoundConversations
        .Include(c => c.Report).Include(c => c.Owner).Include(c => c.OtherUser)
        .FirstOrDefaultAsync(c => c.Id == conversationId && (c.OwnerId == me || c.OtherUserId == me));

    // Starts a conversation about a report (or continues the one this user already has) with a first message.
    public async Task<ServiceResult<LostFoundConversation>> StartAsync(Guid me, Guid reportId, string? text, string? image = null)
    {
        var message = Clean(text);
        if (message.Length > LostFoundMessage.MaxLength || message.Length == 0 && string.IsNullOrWhiteSpace(image))
        {
            return ServiceResult<LostFoundConversation>.BadRequest($"Write a message of 1 to {LostFoundMessage.MaxLength} characters.");
        }
        var report = await db.LostFoundReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reportId);
        if (report is null)
        {
            return ServiceResult<LostFoundConversation>.NotFound("Report not found.");
        }
        if (report.UserId == me)
        {
            return ServiceResult<LostFoundConversation>.BadRequest("This is your own report. People who reply to it appear under Messages.");
        }
        var conversation = await db.LostFoundConversations.FirstOrDefaultAsync(c => c.ReportId == reportId && c.OtherUserId == me);
        if (conversation is null)
        {
            if (report.Status == LostFoundStatus.Closed)
            {
                return ServiceResult<LostFoundConversation>.Conflict("This report is closed, so new messages cannot be started.");
            }
            conversation = new LostFoundConversation { ReportId = reportId, OwnerId = report.UserId, OtherUserId = me };
            db.LostFoundConversations.Add(conversation);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Started from two tabs at once: use the one that was saved.
                db.ChangeTracker.Clear();
                conversation = await db.LostFoundConversations.FirstAsync(c => c.ReportId == reportId && c.OtherUserId == me);
            }
        }
        var sent = await SendAsync(me, conversation.Id, message, image);
        return sent.Succeeded ? ServiceResult<LostFoundConversation>.Created((await MineAsync(me, conversation.Id))!, "Message sent.") : sent.As<LostFoundConversation>();
    }

    // Every conversation this user is in, newest activity first, with the unread count and the last message.
    public async Task<List<LostFoundThread>> ListAsync(Guid me)
    {
        var conversations = await db.LostFoundConversations.AsNoTracking()
            .Include(c => c.Report).Include(c => c.Owner).Include(c => c.OtherUser)
            .Where(c => c.OwnerId == me || c.OtherUserId == me)
            .OrderByDescending(c => c.LastMessageAt).Take(100)
            .AsSingleQuery().ToListAsync();
        var ids = conversations.Select(c => c.Id).ToList();
        var messages = await db.LostFoundMessages.AsNoTracking().Where(m => ids.Contains(m.ConversationId))
            .Select(m => new { m.Id, m.ConversationId, m.SenderId, m.CreatedAt, m.Text, m.ImageKey }).ToListAsync();
        return conversations.Select(c =>
        {
            var mine = messages.Where(m => m.ConversationId == c.Id)
                .Select(m => new LostFoundMessage { Id = m.Id, ConversationId = m.ConversationId, SenderId = m.SenderId, CreatedAt = m.CreatedAt, Text = m.Text, ImageKey = m.ImageKey }).ToList();
            return new LostFoundThread(c, UnreadFor(c, me, mine), mine.OrderByDescending(m => m.CreatedAt).FirstOrDefault());
        }).ToList();
    }

    public async Task<int> UnreadCountAsync(Guid me) => (await ListAsync(me)).Sum(t => t.Unread);

    // The messages of a conversation (after: only newer ones, for polling), and marks them read.
    public async Task<ServiceResult<(LostFoundConversation Conversation, List<LostFoundMessage> Messages, List<LostFoundMessageReaction> Reactions)>> MessagesAsync(Guid me, Guid conversationId, DateTime? after)
    {
        var conversation = await MineAsync(me, conversationId);
        if (conversation is null)
        {
            return ServiceResult<(LostFoundConversation, List<LostFoundMessage>, List<LostFoundMessageReaction>)>.NotFound("Conversation not found.");
        }
        var query = db.LostFoundMessages.AsNoTracking().Where(m => m.ConversationId == conversationId);
        if (after is { } since)
        {
            query = query.Where(m => m.CreatedAt > since);
        }
        var messages = (await query.OrderByDescending(m => m.CreatedAt).Take(PageSize).ToListAsync()).OrderBy(m => m.CreatedAt).ToList();
        var now = DateTime.UtcNow;
        if (IsOwner(conversation, me)) conversation.OwnerReadAt = now; else conversation.OtherReadAt = now;
        await db.SaveChangesAsync();
        var reactions = await db.LostFoundMessageReactions.AsNoTracking().Where(r => r.Message!.ConversationId == conversationId).ToListAsync();
        return ServiceResult<(LostFoundConversation, List<LostFoundMessage>, List<LostFoundMessageReaction>)>.Ok((conversation, messages, reactions));
    }

    // A message has words, a picture (a data URL of a screenshot or photo), or both.
    public async Task<ServiceResult<LostFoundMessage>> SendAsync(Guid me, Guid conversationId, string? text, string? image = null)
    {
        var message = Clean(text);
        var hasImage = !string.IsNullOrWhiteSpace(image);
        if (message.Length > LostFoundMessage.MaxLength || message.Length == 0 && !hasImage)
        {
            return ServiceResult<LostFoundMessage>.BadRequest($"Write a message of 1 to {LostFoundMessage.MaxLength} characters, or add a picture.");
        }
        if (hasImage && pictures is null)
        {
            return ServiceResult<LostFoundMessage>.BadRequest("Pictures cannot be sent right now.");
        }
        var conversation = await MineAsync(me, conversationId);
        if (conversation is null)
        {
            return ServiceResult<LostFoundMessage>.NotFound("Conversation not found.");
        }
        string? key = null;
        if (hasImage)
        {
            try
            {
                (key, _) = await pictures!.SaveImageAsync(image!);
            }
            catch (ArgumentException e)
            {
                return ServiceResult<LostFoundMessage>.BadRequest(e.Message);
            }
        }
        var now = DateTime.UtcNow;
        var entry = new LostFoundMessage { ConversationId = conversationId, SenderId = me, Text = message, ImageKey = key, CreatedAt = now };
        db.LostFoundMessages.Add(entry);
        conversation.LastMessageAt = now;
        if (IsOwner(conversation, me)) conversation.OwnerReadAt = now; else conversation.OtherReadAt = now;
        var sender = IsOwner(conversation, me) ? conversation.Owner : conversation.OtherUser;
        var recipient = IsOwner(conversation, me) ? conversation.OtherUserId : conversation.OwnerId;
        var item = conversation.Report?.ItemName ?? "your report";
        var preview = message.Length == 0 ? "sent a picture" : message.Length > 120 ? message[..120] + "…" : message;
        // One unread notification per conversation is enough; a burst of messages does not flood the bell.
        if (!await db.Notifications.AnyAsync(n => n.UserId == recipient && n.RelatedId == conversationId && !n.IsRead))
        {
            notifications.Notify(recipient, $"New message about {item}", $"{RewardEngine.FirstName(sender?.Name ?? "")}: {preview}",
                NotificationTypes.Info, $"/lost-found?chat={conversationId}", NotificationCategories.General, me, conversationId);
        }
        try
        {
            await db.SaveChangesAsync();
        }
        catch
        {
            pictures?.DeleteImage(key);
            throw;
        }
        return ServiceResult<LostFoundMessage>.Created(entry, "Message sent.");
    }

    // Sets this user's reaction to a message, replaces it with another one, or (emoji empty, or the same one again)
    // takes it away. Returns every reaction on the message.
    public async Task<ServiceResult<List<LostFoundMessageReaction>>> ReactAsync(Guid me, Guid conversationId, Guid messageId, string? emoji)
    {
        emoji = string.IsNullOrWhiteSpace(emoji) ? null : emoji.Trim();
        if (emoji is not null && !LostFoundMessageReaction.Allowed.Contains(emoji))
        {
            return ServiceResult<List<LostFoundMessageReaction>>.BadRequest($"Choose one of: {string.Join(" ", LostFoundMessageReaction.Allowed)}.");
        }
        if (await MineAsync(me, conversationId) is null)
        {
            return ServiceResult<List<LostFoundMessageReaction>>.NotFound("Conversation not found.");
        }
        if (!await db.LostFoundMessages.AnyAsync(m => m.Id == messageId && m.ConversationId == conversationId))
        {
            return ServiceResult<List<LostFoundMessageReaction>>.NotFound("Message not found.");
        }
        var mine = await db.LostFoundMessageReactions.FirstOrDefaultAsync(r => r.MessageId == messageId && r.UserId == me);
        if (emoji is null || mine?.Emoji == emoji)
        {
            if (mine is not null) db.LostFoundMessageReactions.Remove(mine);
        }
        else if (mine is null)
        {
            db.LostFoundMessageReactions.Add(new LostFoundMessageReaction { MessageId = messageId, UserId = me, Emoji = emoji });
        }
        else
        {
            mine.Emoji = emoji;
            mine.CreatedAt = DateTime.UtcNow;
        }
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A double tap: the other request already saved this person's reaction.
            db.ChangeTracker.Clear();
        }
        var all = await db.LostFoundMessageReactions.AsNoTracking().Where(r => r.MessageId == messageId).ToListAsync();
        return ServiceResult<List<LostFoundMessageReaction>>.Ok(all);
    }
}
