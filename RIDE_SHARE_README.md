# Ride Share (F11)

**Ride share** in the top navigation (`/ride-share`) lets you browse open rides and filter them by destination.
Signed-in users can post a ride. Under **My rides** you see your own posts, open and completed, and can
complete or delete them.

## Rules

- **Browsing is open to everyone.** Posting, deleting, completing and joining need a signed-in user. That user is always taken from the token; any `userId`, `userName` or `requesterId` in the request body is ignored.
- **A post needs all four fields:**
  - a source and a destination: not blank, at most 200 characters, trimmed;
  - `maxParticipants`: a whole number from 2 to `RideShare:MaxParticipants` (default 10), counting the host;
  - `genderPreference`: `Any`, `Male` or `Female`.
- **Only the host can delete or complete a post.** Anyone else gets 403.
- **Delete is a soft delete.** It sets `deleted_at`, and the post disappears from every list. A second delete returns 404.
- **Complete changes Open to Completed only once.** It sets `completed_at` and leaves the open list. Completing again returns the same completed post. The gem reward is paid only once, even when two requests arrive at the same moment.
- **Status values.** A ride is stored as `active` (shown as **Open**) or `completed`. The value `active` was kept so existing data and clients keep working.
- **Join requests** (an existing feature) are kept, now tied to the signed-in user. A ride marked Men only or Women only refuses riders of another gender. Only the host can see and answer the requests for a ride.

## Endpoints

| Method | Path | Notes |
|---|---|---|
| GET | `/api/rideshare?destination=&page=1&pageSize=50` | Open rides, newest first. The destination matches anywhere, ignores case and trims spaces. Returns `{ total, page, pageSize, totalPages, data }`. |
| POST | `/api/rideshare` | Needs a token. Body `{ "source": "Mirpur 10", "destination": "Dhanmondi", "maxParticipants": 3, "genderPreference": "Any" }`. Returns 201 `{ data: ride }`. |
| GET | `/api/rideshare/user/{userId}` | Your own call returns your open and completed posts. Anyone else gets only that host's open posts. |
| DELETE | `/api/rideshare/{postId}` | Host only. 200, 403 or 404. |
| POST | `/api/rideshare/complete` | Host only. Body `{ "postId": "…" }`. Returns 200 `{ data: ride }`. |

A ride in a response looks like this:

```json
{ "_id": "…", "source": "Mirpur 10", "destination": "Dhanmondi", "maxParticipants": 3, "genderPreference": "Any",
  "status": "active", "isOpen": true, "host": { "id": "…", "name": "Rafi", "gender": "Male", "verified": false },
  "createdAt": "…", "updatedAt": "…", "completedAt": null }
```

The host's email and phone number are never included.

## Database (migration `AddRideShareLifecycle`)

**New columns:** `gender_preference` (default `Any`), `updated_at`, `completed_at` and `deleted_at`.

**Checks:**
- `status` must be `active` or `completed`;
- `gender_preference` must be `Any`, `Male` or `Female`;
- `max_participants` must be between 2 and 20;
- `source` and `destination` must not be blank.

**Indexes:**
- `ix_ride_posts_open` on `created_at`, covering only open, undeleted posts;
- `(user_id, created_at)` for a host's posts.

**What happens to existing posts:** `updated_at` is set to `created_at`. Completed posts get `completed_at`. Blank
places become "Not specified", and participant counts are pulled into the 2–20 range.

**Rolling back:** run `dotnet ef database update AddChatReadStateAndPairOrder`. Posts deleted since this
migration are removed for good, because the old code had no `deleted_at`.

## Join requests (F12)

Riders press **Join ride** on someone else's open ride. The host opens **View ride requests** on their own card to
accept or decline.

The ride's participants are its accepted requests plus the host; there is no separate participant table.
`maxParticipants` counts the host, so a ride for 4 has 3 places for riders. Rides report `participantCount`
(including the host) and `seatsLeft`.

| Method | Path | Notes |
|---|---|---|
| POST | `/api/riderequests` | Body `{ "ridePostId": "…" }`. The rider is the signed-in user. Returns 201 with a `pending` request. `/api/riderequests/send` still works as the old path. |
| GET | `/api/riderequests/ride/{rideId}` | Host only. Returns `{ ride: { participantCount, seatsLeft, … }, data: [requests] }`. |
| GET | `/api/riderequests/user/{userId}` | Only your own requests, each with a summary of its ride. |
| PUT | `/api/riderequests/accept/{requestId}` | Host only. Returns `{ data: request, ride: { participantCount, seatsLeft } }`. |
| PUT | `/api/riderequests/reject/{requestId}` | Host only. |

**Errors**

| Status | When |
|---|---|
| 400 | Joining your own ride. |
| 401 | Not signed in. |
| 403 | Not the host, or the ride's gender preference does not allow you. |
| 404 | The ride or the request does not exist. |
| 409 | Already requested or already joined; the ride is completed or full; or the request was already handled. |

**Rules**

- **One live request per rider per ride.** A unique index on `(ride_post_id, requester_id)` covers pending and accepted requests (`ix_ride_requests_one_live_per_rider`). Rejected requests are left out, so a declined rider may ask again.
- **No overfilling.** Accepting runs in a transaction that locks the ride's row (`SELECT … FOR UPDATE`). It then counts the accepted riders and moves the request from pending to accepted only if it is still pending. Two accepts at the same moment run one after the other, so a ride never takes more riders than it has places.
- **No repeats.** Accepting or declining again returns 409. It adds no second participant and sends no second notification.
- **Notifications.** On accept or decline, the requester (never the host) gets a notification through the existing `NotificationService`: "Your ride join request has been accepted." or "… has been rejected." It appears under More → Notifications. A new request notifies the host.

Migration `AddRideJoinRequestRules` adds `updated_at`, a status check, the unique index and `(requester_id, created_at)`.
It first tidies existing rows: unknown statuses and hosts' requests for their own rides become `rejected`. Where a
rider had several live requests for one ride, the accepted one (or else the oldest pending one) is kept. Roll back
with `dotnet ef database update AddRideShareLifecycle`.

## Limitation

The destination filter uses `ILIKE '%…%'`. With very many open posts, a trigram index (`pg_trgm`) would make it
faster. It was left out so the migration does not depend on a database extension.
