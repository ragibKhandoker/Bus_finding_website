# One-to-One Chat (F10)

Friends (see F09) can send each other private messages. To start a chat, open **Friends** and press
**Message** next to a friend. All conversations are on **More → Messages** (`/messages`).

## Rules

- **Friends only.** You can open a chat, or send a message, only with someone who is your friend. Anyone else gets a 403.
- **No access to other people's chats.** If you are not in a conversation, it is reported as not found (404), so trying chat ids reveals nothing.
- **One conversation per pair.** It is stored with the smaller user id first (`ck_chats_ordered_pair`), and the unique index on the pair prevents a second one. When two people open the chat at the same moment, the insert uses `ON CONFLICT DO NOTHING`, so both get the same conversation.
- **Messages are 1–2000 characters after trimming.** Blank and whitespace-only messages are refused by the page, by the API (400) and by the database (`ck_chat_messages_text_not_blank`).
- **The sender is always the signed-in user from the token.** A `senderId` sent by the client is ignored.
- **Sending is limited** to 60 messages per minute per user.

## Read receipts

Each message has `read` and `read_at`. Only the recipient can set them, using `POST /api/chat/{chatId}/read`.
Loading messages does not mark anything as read.

The page passes `?upTo=<newest message on screen>`. A message that arrives a moment later therefore stays
unread. Marking a conversation read changes only that conversation, and repeating the call does nothing
more. Your own messages are never counted as unread. A partial index (`ix_chat_messages_unread`) keeps
unread counts fast.

## Endpoints (all require `Authorization: Bearer <token>`)

| Method | Path | Result |
|---|---|---|
| GET | `/api/chat` | `{ chats: [{ _id, otherParticipant, lastMessage, unreadCount, lastMessageAt }] }`, most recent activity first |
| GET | `/api/chat/unread-counts` | `{ total, conversations: [{ chatId, otherUserId, unreadCount }], unreadCounts: { <userId>: n } }` |
| GET | `/api/chat/{friendUserId}` | Opens the conversation: 201 if it was created, 200 if it already existed. Returns `{ created, chat: { _id, otherParticipant, messages, hasMoreMessages, unreadCount } }` |
| GET | `/api/chat/messages/{chatId}?before=&after=&limit=` | `{ messages, hasMore }`, oldest first. Gives the latest page by default; `before` loads older messages and `after` loads newer ones. The default limit is 50 and the maximum is 100. |
| POST | `/api/chat/send` body `{ "chatId": "…", "content": "Hello" }` | 201 `{ message }`. Older clients may send `text` instead of `content`. |
| POST | `/api/chat/{chatId}/read?upTo={messageId}` | `{ marked }` |

The page is ordered by `(created_at, id)`, so paging never skips or repeats a message. People appear only by
their public profile: name, friend code, bio and verified status. Email and phone are never included.

## Updates

New messages arrive by polling. There are no WebSockets. While the Messages page is open and the browser tab
is visible, the open conversation is checked every 5 seconds and the conversation list every 15 seconds. The
timer stops when you leave the page.

## Migration `AddChatReadStateAndPairOrder`

- Adds `read_at` and fills it with `created_at` for messages that were already read.
- Re-orders existing chats into the new pair order. If a pair has two rows, the messages are merged into one conversation.
- Deletes chats with oneself and whitespace-only messages, both of which the old code could save.
- Adds the check constraints and the unread index.

Roll back with `dotnet ef database update AddFriendRequestFlow`. The constraints and `read_at` are removed;
the re-ordered rows stay as they are, which the old code also accepts.
