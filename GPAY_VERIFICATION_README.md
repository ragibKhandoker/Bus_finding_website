# GPay & Account Verification (F16)

## GPay (More → GPay & recharge, `/gpay`)

GPay is an **in-app demo balance**. It is not connected to any real payment provider, and the page says so.

- **Starting balance.** An account starts with `Rewards:GpayStartingBalance` from `appsettings.json` (currently 5000). Each user can have one account, enforced by a unique index on `gpay_accounts.user_id`. Creating it shows a 4-digit PIN once; only a hash of the PIN is stored.
- **The PIN.** Every money movement needs the PIN; `recharge`, `pay`, `deduct` and `login` are rate-limited. A forgotten PIN is replaced with `POST /gpay/reset-pin`, which needs the account password.
- **Ways money moves:**
  - **Move to wallet** (`recharge`) takes money from GPay and credits the commute wallet. Both sides are recorded: the GPay ledger and the wallet ledger.
  - **Pay a trip** (`pay`) pays a completed trip's fare, as the server recorded it when the trip ended. Each trip can be paid once.
  - **Admin test top-up** (`admin/top-up`) is the only way money is added after the starting balance, because there is no payment gateway. Real-money top-ups need a payment provider with server-side confirmation (a webhook); none is configured.
- **Keeping money safe.**
  - Each movement is one database transaction: a conditional `UPDATE … SET balance = balance - x WHERE balance >= x`, plus the ledger entry, plus any wallet credit. Simultaneous requests cannot overspend, and the database refuses a negative balance (`ck_gpay_accounts_balance_not_negative`).
  - The page sends a new random `reference` with each button press; a repeat with the same reference returns the first result.
  - `ix_gpay_transactions_one_per_trip` stops a trip being paid twice.

| Method | Path | Notes |
|---|---|---|
| POST | `/gpay/register` | Returns 201 with `data.displayCode` (the PIN, shown once). |
| POST | `/gpay/login` | Body `{ "code": "1234" }`. Only checks the PIN. |
| POST | `/gpay/reset-pin` | Body `{ "password": "…" }`. Returns a new PIN once. |
| GET | `/gpay/balance` | Returns `data.balance` and `startingBalance`. |
| GET | `/gpay/transactions?page=&pageSize=` | The ledger: type, signed amount, balance after, description, trip, date. |
| GET | `/gpay/trips` | Completed trips with fare and `paid`. |
| POST | `/gpay/recharge` | Body `{ amount, pin, reference? }`. |
| POST | `/gpay/pay` | Body `{ tripId, pin, reference? }`. The fare comes from the trip. |
| POST | `/gpay/deduct` | Older clients. Body `{ amount, pin, description?, reference? }`. |
| POST | `/gpay/admin/top-up` | Admin only. Body `{ userId, amount, reference? }`. |

Amounts must be more than 0, at most ৳100,000, and have at most 2 decimals. A wrong PIN or insufficient balance
returns 400; a trip that is already paid returns 409.

## Account verification (More → Account verification, `/verification`)

### Users

Choose **Student pass** (institution name and student ID required) or **NID**, then pick a photo. The upload is
checked in two places:

- **On the page:** the type (JPEG, PNG or WebP) and the size, up to 5 MB.
- **On the server:**
  - the declared type must be one of those three;
  - the first bytes must really be that format, so a renamed PDF or SVG is refused;
  - the size must be within `Verification:MaxImageBytes`.

The photo is uploaded, then the request is submitted with its `imageId`. A user can have one pending request at a
time (unique index). A rejected request can be resubmitted; an approved document type cannot. The page shows
Pending, Approved or Rejected (with the reason) from the server.

### Admins

The same page shows a review queue (Pending / Approved / Rejected / All), and the Admin page links to it with a
pending count. **View photo** loads the document with the admin's token.

- **Approve** sets the request to approved and marks the user `Pass` (student) or `IdentityVerified` (NID).
- **Reject** needs a reason, which the user sees.

Both record the reviewer and time, and notify the user. A request can only be decided while it is pending, so a
repeated click returns 409. The server checks the admin role on `all`, `approve` and `reject`.

### Where documents are kept

Files are written to `Verification:StoragePath` (default `App_Data/verification-images`, outside `wwwroot`) with
random names; a user-supplied file name is never used. That folder is ignored by git and Docker.

The public `/uploads` file server was **removed**. Photos are only streamed by `GET /verify/images/{id}` and
`GET /verify/{requestId}/image`, to their owner or an admin, with `Cache-Control: private, no-store`. Older
photos in `wwwroot/uploads` are still readable through the second endpoint.

On hosts whose disk is wiped on redeploy (such as Render's free tier), point `StoragePath` at a persistent disk or
replace `ImageStorageService` with private cloud storage.

| Method | Path | Who |
|---|---|---|
| POST | `/verify/upload-image` | Signed-in user. Body `{ "image": "data:image/jpeg;base64,…" }`. Returns 201 `{ imageId }`. |
| POST | `/verify/submit` | Signed-in user. Body `{ documentType: "student_pass" or "nid", imageId, institutionName?, institutionId?, gmail? }`. |
| GET | `/verify/my-status` | Signed-in user. Returns `data` (the latest request, with `state` and `rejectionReason`), `studentPass` and `nid`. |
| GET | `/verify/images/{imageId}`, `/verify/{id}/image` | The owner or an admin. |
| GET | `/verify/all?status=pending&page=&pageSize=` | Admin. |
| POST | `/verify/approve` | Admin. Body `{ verificationId }`. |
| POST | `/verify/reject` | Admin. Body `{ verificationId, reason }`. |

## Migration `AddGpayLedgerAndPrivateVerification`

- **New tables:** `gpay_transactions` (each existing account's balance is written as its opening entry) and `verification_images`.
- **New columns:** on verifications, `document_type` (existing rows become `student_pass`), `image_id`, `rejection_reason` and `reviewed_by_id`; on users, `identity_verified`.
- **Constraints and indexes:** the checks and indexes listed above.
- **Data clean-up:** if a user had several pending requests, the newest is kept and the others are marked rejected.

Nothing is deleted. Roll back with `dotnet ef database update AddOfferLedger`.
