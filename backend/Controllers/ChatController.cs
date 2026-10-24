using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartCommute.Server.Data;
using SmartCommute.Server.Models;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

[Authorize]
[Route("api/chat")]
public class ChatController(AppDbContext db) : ApiControllerBase
{
    // Conversation list for the signed-in user, newest activity first.
    [HttpGet("")]
    public async Task<IActionResult> List()
    {
        var me = CurrentUserId;
        var chats = await db.Chats
            .Include(c => c.User1)
            .Include(c => c.User2)
            .Where(c => c.User1Id == me || c.User2Id == me)
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .AsNoTracking()
            .ToListAsync();

        var chatIds = chats.Select(c => c.Id).ToList();
        var lastMessages = await db.ChatMessages
            .Where(m => chatIds.Contains(m.ChatId))
            .GroupBy(m => m.ChatId)
            .Select(g => g.OrderByDescending(m => m.CreatedAt).First())
            .ToDictionaryAsync(m => m.ChatId);
        var unread = await db.ChatMessages
            .Where(m => chatIds.Contains(m.ChatId) && m.SenderId != me && !m.Read)
            .GroupBy(m => m.ChatId)
            .Select(g => new { ChatId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ChatId, x => x.Count);

        var summaries = chats.Select(c => new ChatSummaryViewModel
        {
            Id = c.Id.ToString(),
            OtherParticipant = PersonViewModel.From(c.User1Id == me ? c.User2! : c.User1!),
            LastMessage = lastMessages.TryGetValue(c.Id, out var last) ? ChatMessageViewModel.From(last) : null,
            UnreadCount = unread.GetValueOrDefault(c.Id),
            LastMessageAt = c.LastMessageAt,
        });
        return Ok(new { success = true, chats = summaries });
    }

    // Unread message counts keyed by the other person's user id.
    [HttpGet("unread-counts")]
    public async Task<IActionResult> UnreadCounts()
    {
        var me = CurrentUserId;
        var counts = await db.ChatMessages
            .Where(m => (m.Chat!.User1Id == me || m.Chat.User2Id == me) && m.SenderId != me && !m.Read)
            .GroupBy(m => m.SenderId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync();
        return Ok(new { success = true, unreadCounts = counts.ToDictionary(c => c.UserId.ToString(), c => c.Count) });
    }

    [HttpGet("messages/{chatId:guid}")]
    public async Task<IActionResult> Messages(Guid chatId)
    {
        var me = CurrentUserId;
        var chat = await db.Chats.FindAsync(chatId);
        if (chat is null || !chat.HasParticipant(me))
        {
            return NotFoundError("Chat not found.");
        }

        await db.ChatMessages
            .Where(m => m.ChatId == chatId && m.SenderId != me && !m.Read)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Read, true));

        var messages = await db.ChatMessages
            .Where(m => m.ChatId == chatId)
            .OrderBy(m => m.CreatedAt)
            .AsNoTracking()
            .ToListAsync();
        return Ok(new { success = true, messages = messages.Select(ChatMessageViewModel.From) });
    }

    [HttpPost("send")]
    public async Task<IActionResult> Send(SendMessageRequest request)
    {
        var me = CurrentUserId;
        var chat = await db.Chats.FindAsync(request.ChatId);
        if (chat is null || !chat.HasParticipant(me))
        {
            return NotFoundError("Chat not found.");
        }

        var message = new ChatMessage { ChatId = chat.Id, SenderId = me, Text = request.Text.Trim() };
        db.ChatMessages.Add(message);
        chat.LastMessageAt = message.CreatedAt;
        await db.SaveChangesAsync();
        return Created201(new { success = true, message = ChatMessageViewModel.From(message) });
    }

    // Gets (or starts) the conversation with another user.
    [HttpGet("{otherUserId:guid}")]
    public async Task<IActionResult> GetOrCreate(Guid otherUserId)
    {
        var me = CurrentUserId;
        if (otherUserId == me)
        {
            return BadRequestError("You cannot chat with yourself.");
        }

        var users = await db.Users.Where(u => u.Id == me || u.Id == otherUserId).ToDictionaryAsync(u => u.Id);
        if (!users.ContainsKey(otherUserId) || !users.ContainsKey(me))
        {
            return NotFoundError("User not found.");
        }

        var (user1, user2) = me.CompareTo(otherUserId) < 0 ? (me, otherUserId) : (otherUserId, me);
        var chat = await db.Chats.FirstOrDefaultAsync(c => c.User1Id == user1 && c.User2Id == user2);
        if (chat is null)
        {
            chat = new Chat { User1Id = user1, User2Id = user2 };
            db.Chats.Add(chat);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Both users opened the chat at the same moment; use the row the other request created.
                db.Entry(chat).State = EntityState.Detached;
                chat = await db.Chats.FirstAsync(c => c.User1Id == user1 && c.User2Id == user2);
            }
        }

        var messages = await db.ChatMessages
            .Where(m => m.ChatId == chat.Id)
            .OrderBy(m => m.CreatedAt)
            .AsNoTracking()
            .ToListAsync();

        return Ok(new
        {
            success = true,
            chat = new ChatViewModel
            {
                Id = chat.Id.ToString(),
                Participants = [PersonViewModel.From(users[user1]), PersonViewModel.From(users[user2])],
                Messages = messages.Select(ChatMessageViewModel.From).ToList(),
                LastMessageAt = chat.LastMessageAt,
                CreatedAt = chat.CreatedAt,
            },
        });
    }
}
