# Friend Codes & Friend Requests (F09)

Every user gets a permanent 5-character friend code (A–Z, 0–9). Others search for that code, send a
friend request, and the recipient accepts or declines it. The web page is **More → Friends** (`/friends`).

## Backend

| Piece | File |
|---|---|
| Code generation (crypto RNG, format checks) | `backend/Services/FriendCodeGenerator.cs` |
| Request / accept / reject / list rules | `backend/Services/FriendService.cs` |
| `/friends/*` endpoints | `backend/Controllers/FriendsController.cs` |
| `/auth/friend-code`, `/auth/search-friend` | `backend/Controllers/AuthController.cs` |
| Entities | `backend/Models/Friend.cs`, `backend/Models/User.cs` |

The signed-in user always comes from the JWT. The client never sends a sender or owner id.

### Endpoints (all need `Authorization: Bearer <token>`)

| Method | Path | Result |
|---|---|---|
| GET | `/auth/friend-code` | `{ friendCode }` |
| GET | `/auth/search-friend?code=R7K2M` | `{ user, relationship, requestId }`. 400 for a bad format, 404 for an unknown code. Limited to 30 searches per minute per user. |
| POST | `/friends/request` body `{ "friendCode": "R7K2M" }` | 201 `{ data: request }`. 400 for yourself, 404 for an unknown code, 409 if you are already friends or a request is already pending. |
| GET | `/friends/requests/incoming` | Pending requests sent to you |
| GET | `/friends/requests/sent` | Your pending requests |
| POST | `/friends/requests/{id}/accept` | 200. 403 if you are not the recipient, 404 if there is no such request, 409 if it was already handled. |
| POST | `/friends/requests/{id}/reject` | Same rules as accept |
| GET | `/friends` | Your friends, ordered by name |

The list endpoints take `?page=1&pageSize=50` (at most 100). They return `{ page, pageSize, total, totalPages, data }`.

`relationship` is one of `none`, `self`, `friends`, `request_sent` or `request_received`. If someone has
already sent you a request, sending one back gives a 409 that asks you to accept theirs instead.

Search results and request lists show only a person's public profile: `id`, `name`, `friendCode`, `bio`,
`verified` and `memberSince`. They never include an email address, phone number or password hash.

The older Flutter endpoints still work and use the same rules: `/auth/send-friend-request`,
`/auth/pending-friend-requests`, `/auth/accept|reject-friend-request/{id}`, `/auth/friends`,
`/auth/search-friend/{code}`, `/api/friends` and `/api/friends/add`. There are two changes:
`/api/friends/add` now sends a request instead of adding the friend directly, and duplicate requests now
return 409 instead of 400.

## Database rules (migration `AddFriendRequestFlow`)

- `users.friend_code` must be unique (`ix_users_friend_code`) and must match `^[A-Z0-9]{5}$` (`ck_users_friend_code_format`).
- Each friendship is one row in `friendships (user1id, user2id)`, with the smaller id first (`ck_friendships_ordered_pair`). The primary key on that pair stops a duplicate friendship. Each user's friend list reads the row from either side.
- `friend_requests` has an `updated_at` column. A user cannot send a request to themselves (`ck_friend_requests_not_self`). The status must be `pending`, `accepted` or `rejected`. `ix_friend_requests_one_pending_per_pair` allows only one pending request per pair, in either direction.
- Accepting a request runs in one transaction: the request changes only if it is still pending, and the friendship insert uses `ON CONFLICT DO NOTHING`. Two accepts sent at the same time cannot create two friendships.
- At sign-up, if another user takes the same friend code at the same moment, the unique index rejects the insert and the server retries with a new code.

### What the migration does to existing data

1. Friendships stored as two rows, (A,B) and (B,A), are merged into one ordered row.
2. Requests to yourself are deleted. A pending request between users who are already friends is marked `accepted`. If a pair has several pending requests, the oldest stays pending and the others become `rejected`. `updated_at` is set to `created_at`.
3. Any user with a missing or malformed friend code gets a new unique one.

No users, friendships or pending requests are lost.

### Applying and rolling back

The server applies pending migrations when it starts. To apply them by hand:

```bash
cd backend
dotnet tool restore
dotnet ef database update
```

To roll back to the migration before this one, run:

```bash
dotnet ef database update AddWalletLedgerBalancesAndChecks
```

This restores two rows per friendship and drops the new constraints, indexes and `updated_at`. Back up
the database before you roll back a production database.

## Tests

```bash
# backend: unit tests always run; integration tests need a PostgreSQL login that can create databases
cd tests/SmartCommute.Tests
SMARTCOMMUTE_TEST_DB="Host=localhost;Username=postgres;Password=..." dotnet test
# frontend
cd frontend && npm test
```
