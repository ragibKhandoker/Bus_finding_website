# Bus Reviews & Ratings (F13)

Reviews appear on every bus's page (`/bus-routes/{route}`, open by default) and under **Community reviews**
on each Find-a-bus result. Anyone can read them. Signing in lets you rate a bus from 1 to 5 stars, write a
review, reply, and like or dislike reviews. You can edit and delete your own reviews.

## How it works

- **The rating lives on the review.** `reviews.rating` is a whole number from 1 to 5. The API refuses 0, 6, 4.5, text and a missing value. The database refuses anything outside 1–5 (`ck_reviews_rating_range`).
- **The average is calculated by the database.** `ReviewService.SummaryAsync` groups a bus's rated reviews by star and returns:
  - `averageRating`, rounded to 1 decimal, or `null` when the bus has no ratings (no division by zero);
  - `totalRatings`;
  - `distribution` from 1 to 5 stars.

  Replies and reactions have no rating, so they never count. The summary is returned with the review list and after every create, edit and delete, so the page always shows the server's numbers.
- **Who can change what.** Only the author can edit a review. The author or an admin can delete it, and its replies and reactions go with it (cascading foreign keys). Replies can be deleted by their author or an admin. The author is always the signed-in user from the token; any `userId` in the body is ignored.
- **Replies.** Replies sit in `review_replies` under one review. They cannot be replied to. If a reply sends `busId`, it must match the review's bus.
- **Reactions.** Each (review, user) pair has at most one row; the primary key is `(review_id, user_id)`. Clicking a reaction runs one transaction:
  1. `DELETE` the same reaction. If a row was deleted, the reaction is now off.
  2. Otherwise, `INSERT … ON CONFLICT DO UPDATE`. This adds the reaction, or switches like ↔ dislike.

  The counts are then recounted from the table and returned with `myReaction` (`like`, `dislike` or `null`). The list also includes each reader's own `myReaction`, so the buttons are right after a refresh.

## Endpoints

| Method | Path | Notes |
|---|---|---|
| GET | `/api/reviews/bus/{busId}?page=1&pageSize=20` | Public. Returns `{ total, totalPages, summary, reviews: [{ _id, author, rating, comment, likes, dislikes, myReaction, repliesList, createdAt, updatedAt, edited }] }` |
| POST | `/api/reviews` | Sign-in needed. Body `{ "busId": "…", "rating": 4, "comment": "Clean bus" }`. Returns 201 `{ review, summary }` |
| PUT | `/api/reviews/{id}` | Author only. Body `{ "rating"?: 1-5, "comment"?: "…" }`. Returns `{ review, summary }` |
| DELETE | `/api/reviews/{id}` | Author or admin. Returns `{ summary }` |
| POST | `/api/reviews/{id}/reply` | Body `{ "comment": "…", "busId"?: "…" }`. Returns 201 `{ reply }` |
| DELETE | `/api/reviews/replies/{replyId}` | Reply author or admin |
| POST | `/api/reviews/{id}/like` and `/dislike` | Toggle. Returns `{ likes, dislikes, myReaction }` |
| GET | `/api/ratings/bus/{busId}` | `{ data: { averageRating, totalRatings, totalReviews, distribution } }` |
| GET | `/api/ratings/all` | Average and count per bus, keyed by bus id |
| POST | `/api/ratings` | Older clients. Sign-in needed. Body `{ busId, rating }` sets the user's rating on their latest review of that bus, or adds a rating-only review. |

Text is trimmed and must be 1–1000 characters. The page shows it as plain text.

**Errors:**
- 400: bad input;
- 401: not signed in;
- 403: someone else's review;
- 404: the bus, review or reply does not exist.

## Migration `AddReviewRatings`

1. Adds `reviews.rating` with its check constraint.
2. Adds the indexes `ix_reviews_rated` and `(review_id, created_at)` on replies.
3. Copies every row of the old `ratings` table onto a review with the same bus, user, comment and times. Fractional values are rounded to whole stars.
4. Recounts the cached `likes`/`dislikes` from `review_reactions`.

The `ratings` table is kept but no longer used. Roll back with `dotnet ef database update AddRideJoinRequestRules`.
That removes the copied reviews and the column. Ratings given on reviews after the migration are lost on rollback.
