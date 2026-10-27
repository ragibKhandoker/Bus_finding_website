using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartCommute.Server.Models;
using SmartCommute.Server.Services;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

// One-to-one chat between friends. The signed-in user always comes from the token. Reading messages never marks
// them read; the client calls POST /api/chat/{chatId}/read for the messages it has shown.
[Authorize]
[Route("api/chat")]
public class ChatController(ChatService chats) : ApiControllerBase
{
    // Conversation list for the signed-in user, newest activity first.
    [HttpGet("")]
    public async Task<IActionResult> List()
    {
        var me = CurrentUserId;
        var list = await chats.ListAsync(me);
        var lastMessages = await chats.LastMessagesAsync(list.Select(c => c.Id).ToList());
        var unread = (await chats.UnreadCountsAsync(me)).ToDictionary(u => u.ChatId, u => u.Count);

        var summaries = list.Select(c => new ChatSummaryViewModel
        {
            Id = c.Id.ToString(),
            OtherParticipant = FriendProfileViewModel.From(c.OtherUser(me)!),
            LastMessage = lastMessages.TryGetValue(c.Id, out var last) ? ChatMessageViewModel.From(last) : null,
            UnreadCount = unread.GetValueOrDefault(c.Id),
            LastMessageAt = c.LastMessageAt,
            CreatedAt = c.CreatedAt,
        });
        return Ok(new { success = true, chats = summaries });
    }

    // Unread counts per conversation and in total. unreadCounts (keyed by the other person's user id) is kept for
    // older clients.
    [HttpGet("unread-counts")]
    public async Task<IActionResult> UnreadCounts()
    {
        var counts = await chats.UnreadCountsAsync(CurrentUserId);
        return Ok(new
        {
            success = true,
            total = counts.Sum(c => c.Count),
            conversations = counts.Select(c => new { chatId = c.ChatId.ToString(), otherUserId = c.OtherUserId.ToString(), unreadCount = c.Count }),
            unreadCounts = counts.ToDictionary(c => c.OtherUserId.ToString(), c => c.Count),
        });
    }

    // A page of messages, oldest first: the latest page by default, ?before={messageId} for older ones,
    // ?after={messageId} for newer ones. ?limit= sets the page size (default 50, at most 100).
    [HttpGet("messages/{chatId:guid}")]
    public async Task<IActionResult> Messages(Guid chatId, [FromQuery] Guid? before, [FromQuery] Guid? after, [FromQuery] int limit = ChatService.DefaultPageSize)
    {
        var result = await chats.MessagesAsync(CurrentUserId, chatId, before, after, limit);
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        return Ok(new { success = true, messages = result.Value!.Messages.Select(ChatMessageViewModel.From), hasMore = result.Value.HasMore });
    }

    [EnableRateLimiting("chat-send")]
    [HttpPost("send")]
    public async Task<IActionResult> Send(SendMessageRequest request)
    {
        var result = await chats.SendAsync(CurrentUserId, request.ChatId!.Value, request.Content ?? request.Text);
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        return Created201(new { success = true, message = ChatMessageViewModel.From(result.Value!) });
    }

    // Marks the other person's messages in this chat as read: all of them, or up to ?upTo={messageId}.
    [HttpPost("{chatId:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid chatId, [FromQuery] Guid? upTo)
    {
        var result = await chats.MarkReadAsync(CurrentUserId, chatId, upTo);
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        return Ok(new { success = true, marked = result.Value });
    }

    // Opens the conversation with a friend: the existing one if there is one (200), otherwise a new one (201).
    // The response includes the latest page of messages.
    [HttpGet("{otherUserId:guid}")]
    public async Task<IActionResult> Open(Guid otherUserId)
    {
        var me = CurrentUserId;
        var result = await chats.OpenAsync(me, otherUserId);
        if (!result.Succeeded)
        {
            return Error(result.StatusCode, result.Message);
        }
        var (chat, created) = result.Value;
        var page = (await chats.MessagesAsync(me, chat.Id, null, null, ChatService.DefaultPageSize)).Value!;
        var unread = (await chats.UnreadCountsAsync(me)).Where(u => u.ChatId == chat.Id).Sum(u => u.Count);
        var body = new
        {
            success = true,
            created,
            chat = new ChatViewModel
            {
                Id = chat.Id.ToString(),
                Participants = [FriendProfileViewModel.From(chat.User1!), FriendProfileViewModel.From(chat.User2!)],
                OtherParticipant = FriendProfileViewModel.From(chat.OtherUser(me)!),
                Messages = page.Messages.Select(ChatMessageViewModel.From).ToList(),
                HasMoreMessages = page.HasMore,
                UnreadCount = unread,
                LastMessageAt = chat.LastMessageAt,
                CreatedAt = chat.CreatedAt,
            },
        };
        return created ? Created201(body) : Ok(body);
    }
}
