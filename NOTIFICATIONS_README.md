# Notifications & SOS (F14)

The bell in the top bar shows how many notifications you have not read; past 99 it shows `99+`. It opens
**More → Notifications** (`/notifications`). The count is fetched when you sign in, every 30 seconds while the
tab is visible, when the window regains focus, and straight after any change on the Notifications page. There
are no WebSockets in the project, so this is polling.

## Endpoints

All of these need `Authorization: Bearer <token>`. A `{userId}` in the path must be your own id, or you get 403.
Someone else's notification id returns 404, so ids cannot be probed.

| Method | Path | Result |
|---|---|---|
| GET | `/api/notifications/user/{userId}?page=1&pageSize=20&unreadOnly=false` | Newest first: `{ total, totalPages, unreadCount, data }` |
| GET | `/api/notifications/unread/{userId}` | `{ count }` |
| PATCH | `/api/notifications/read/{id}` | `{ data, unreadCount }` |
| PATCH | `/api/notifications/read-all/{userId}` | `{ count, unreadCount: 0 }`. The older `PATCH read-all` still works. |
| DELETE | `/api/notifications/{id}` | `{ unreadCount }` |
| DELETE | `/api/notifications/clear/{userId}` | Deletes only your own notifications. The older `DELETE user/{userId}` still works. |
| POST | `/api/notifications/sos` | Body `{ "latitude": 23.81, "longitude": 90.41, "serviceName": "Medical", "message": "On the bus near Farmgate" }`. Returns 201 `{ count }` |

Each notification has:
- `type`: how it looks;
- `category`: `general`, `account`, `friend`, `ride`, `reward` or `sos`;
- `senderId` and `relatedId`;
- `actionUrl`: a page in the app, or a Google Maps link for an SOS.

## When notifications are sent

| Event | Who is told |
|---|---|
| Friend request sent / accepted | The receiver / the sender |
| Ride join request sent | The ride's host |
| Ride request accepted / declined | The requester |
| Ride deleted by its host | The riders who asked to join |
| Gems earned (trip finished, ride share completed) | The user who earned them. This is added in `WalletService.AddGems`, so every gem reward is covered. |
| Profile changed, password reset by admin, student verification decided | That user |
| SOS | See below |

A notification is saved in the same `SaveChanges` call as the change it describes, so a failed action never
leaves a notification behind.

## SOS

- **Before sending.** You choose what help you need and confirm. Only then does the browser ask for your location. If permission is denied, the location cannot be found, the request times out or the browser has no location support, nothing is sent; you see why and can try again. A made-up location is never sent.
- **Who receives it.** Your confirmed friends, plus everyone on your open ride shares: your accepted riders, or the host and other riders of a ride you have joined. Nobody else is told. Before F14, the alert went to every user. If you have no friends or ride companions, the server answers 409 "No alert was sent…" instead of reporting success.
- **What it contains.**
  - Title: "🚨 SOS from {name}".
  - The kind of help and your optional note.
  - A link `https://www.google.com/maps?q=LAT,LON`, always written with plain digits whatever the server's language.
  - The time it was sent.

  The page only turns Google Maps links into clickable links, which open in a new tab with `noopener`.
- **What it does not do.** It is an in-app alert only; no emergency service is contacted, and the page says so ("call 999"). The coordinates are not logged. Sending is limited to 10 per minute.

## Migration `AddNotificationDetails`

Adds `category` (default `general`, with a check constraint), `sender_id` (a foreign key that is set to null if
the sender's account is deleted), `related_id`, and an index on `(user_id, created_at)`. Existing notifications
get a category based on their type and title; nothing is deleted. Roll back with
`dotnet ef database update AddReviewRatings`.
