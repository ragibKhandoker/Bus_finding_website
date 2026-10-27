# Offers: Cashback, Coupons & Discounts (F15)

Each user has three offer balances in taka, shown on **More → Offers & rewards** (`/offers`):

| Balance | How it is used |
|---|---|
| Cashback | Moved into the wallet, all at once or part of it. |
| Coupon | Taken off the fare of one of your completed trips, once per trip, never more than the fare. The saving goes into your wallet. |
| Discount | The same as a coupon, and it can be combined with one until the trip's fare is covered. |

The app works out a trip's fare when the trip ends (`TripService.Fare`) but does not charge it to the wallet. A
coupon or discount therefore pays the saving on that recorded fare back into the wallet as a credit of type
`coupon` or `discount`. If fare payment is added later, the same service call can be made at payment time
instead.

No expiry dates, percentage discounts or minimum fares are configured anywhere in the project, so none are
invented. Admins can pause a user's offers (`isActive = false`); paused offers cannot be used.

## Welcome offers

The amounts come from `Rewards:WelcomeCashback`, `WelcomeCoupon` and `WelcomeDiscount` in `appsettings.json`.

- **New accounts.** `POST /auth/signup` adds the offers, a `welcome:<kind>` ledger entry for each, and a notification. These are saved in the same `SaveChanges` as the user and wallet, so the account exists only together with its welcome offer.
- **Older accounts** get theirs the first time they open their offers.
- **Never twice.** The unique index on `offers.user_id` and the unique `(user_id, reference)` index on the ledger mean welcome offers are given once, even on simultaneous first visits.

## Endpoints

All of these need a token. Write calls return `{ success, message, offers, wallet, entry, transaction, trip }`.

| Method | Path | Who | Notes |
|---|---|---|---|
| GET | `/offers/user` | You | Your balances, `welcome` amounts, `history` (last 30 entries) and `trips` (completed trips with fare, saved and remaining). |
| GET | `/offers/user/{userId}` | You, or an admin | Same shape. |
| POST | `/offers/use-cashback` | You | Body `{ "amount"?: 20, "reference"?: "uuid" }`. Leaving out `amount` moves all of it. |
| POST | `/offers/use-coupon` | You | Body `{ "tripId": "…", "amount"?: 5, "reference"?: "…" }`. |
| POST | `/offers/use-discount` | You | Same body as `use-coupon`. |
| POST | `/offers/add-cashback`, `/add-coupon`, `/add-discount` | Admin | Body `{ "userId": "…", "amount": 25, "reference"?: "promo-1" }`. |
| PUT | `/offers/user` (with `userId` in the body) and `/offers/user/{userId}` | Admin | Body `{ cashback, coupon, discount, isActive }`. Each change is recorded as an `adjusted` entry. |

Amounts must be more than 0, at most 1,000,000 and have at most 2 decimals.

**Errors:**
- 400: bad amount, or not enough cashback;
- 403: someone else's offers, or a non-admin trying to add or set value;
- 404: the trip or user does not exist;
- 409: the offer is already used on this trip, the fare is already covered, nothing is left, the offers are paused, or the trip is not completed.

## How money stays correct

- **The balance and its check are one statement.** Each use runs `UPDATE offers SET <kind> = <kind> - x WHERE user_id = … AND is_active AND <kind> >= x`. That statement locks the row until the transaction ends, so simultaneous requests cannot spend the same taka twice.
- **Everything is saved together or not at all.** The wallet credit (through `WalletService.Credit`, which writes the wallet ledger) and the `offer_transactions` entry are saved in the same database transaction. If the wallet changed at the same moment (its row-version check fails), everything rolls back and the API answers 409.
- **The database refuses repeats.**
  - `ix_offer_transactions_one_per_reference` makes a request repeated with the same `reference` return the first result. The page sends a new random reference with each button press.
  - `ix_offer_transactions_one_kind_per_trip` stops a second coupon (or discount) on the same trip.
- **Balances cannot go negative.** The database checks `ck_offers_*_not_negative`.
- **The server does every calculation.** Saving = min(balance, the fare still uncovered, the requested amount). The client's figures are never used.

## Migration `AddOfferLedger`

Creates `offer_transactions` with its foreign keys, indexes and checks, and adds the non-negative checks on
`offers`. Existing balances are kept: any negative value is set to 0, and each non-zero balance is written to the
ledger as an `opening` entry. Roll back with `dotnet ef database update AddNotificationDetails`.
