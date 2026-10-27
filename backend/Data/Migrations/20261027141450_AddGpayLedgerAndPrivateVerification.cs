using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    // GPay & verification (F16): the gpay_transactions ledger, private verification images, NID as a document type,
    // rejection reasons and reviewers. Existing data is kept: earlier requests are student-pass requests; if a user
    // somehow has several pending requests, the newest stays pending and the others are marked rejected; a negative
    // GPay balance is set to 0; and each GPay account's balance is written to the ledger as its opening entry.
    // Earlier document images stay in wwwroot/uploads but are no longer served publicly (see ImageStorageService).
    /// <inheritdoc />
    public partial class AddGpayLedgerAndPrivateVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "document_type",
                table: "verifications",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "student_pass");

            migrationBuilder.AddColumn<Guid>(
                name: "image_id",
                table: "verifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                table: "verifications",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by_id",
                table: "verifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "identity_verified",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "gpay_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    wallet_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gpay_transactions", x => x.id);
                    table.CheckConstraint("ck_gpay_transactions_type", "type IN ('opening', 'wallet-recharge', 'trip-payment', 'payment', 'top-up')");
                    table.ForeignKey(
                        name: "fk_gpay_transactions_gpay_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "gpay_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_gpay_transactions_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_gpay_transactions_wallet_transactions_wallet_transaction_id",
                        column: x => x.wallet_transaction_id,
                        principalTable: "wallet_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "verification_images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    content_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_verification_images", x => x.id);
                    table.ForeignKey(
                        name: "fk_verification_images_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(@"
UPDATE verifications SET status = 'rejected', rejection_reason = 'Closed when verification was updated.' WHERE status NOT IN ('hold', 'approved', 'rejected');
UPDATE verifications v SET status = 'rejected', reviewed_at = now(), rejection_reason = 'Replaced by a newer request.'
  FROM (SELECT id, row_number() OVER (PARTITION BY user_id ORDER BY created_at DESC, id DESC) AS n
          FROM verifications WHERE status = 'hold') d
 WHERE v.id = d.id AND d.n > 1;
UPDATE gpay_accounts SET balance = 0 WHERE balance < 0;
INSERT INTO gpay_transactions (id, account_id, user_id, type, amount, balance_after, description, created_at)
SELECT gen_random_uuid(), a.id, a.user_id, 'opening', a.balance, a.balance, 'Balance when the GPay history started', now()
  FROM gpay_accounts a;
");

            migrationBuilder.CreateIndex(
                name: "ix_verifications_image_id",
                table: "verifications",
                column: "image_id",
                unique: true,
                filter: "image_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_verifications_one_pending_per_user",
                table: "verifications",
                column: "user_id",
                unique: true,
                filter: "status = 'hold'");

            migrationBuilder.CreateIndex(
                name: "ix_verifications_status_created_at",
                table: "verifications",
                columns: new[] { "status", "created_at" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_verifications_document_type",
                table: "verifications",
                sql: "document_type IN ('student_pass', 'nid')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_verifications_status",
                table: "verifications",
                sql: "status IN ('hold', 'approved', 'rejected')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_gpay_accounts_balance_not_negative",
                table: "gpay_accounts",
                sql: "balance >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_gpay_transactions_account_id",
                table: "gpay_transactions",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_gpay_transactions_one_per_reference",
                table: "gpay_transactions",
                columns: new[] { "user_id", "reference" },
                unique: true,
                filter: "reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_gpay_transactions_one_per_trip",
                table: "gpay_transactions",
                column: "trip_id",
                unique: true,
                filter: "trip_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_gpay_transactions_user_id_created_at",
                table: "gpay_transactions",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_gpay_transactions_wallet_transaction_id",
                table: "gpay_transactions",
                column: "wallet_transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_verification_images_storage_key",
                table: "verification_images",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_verification_images_user_id_created_at",
                table: "verification_images",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_verifications_verification_images_image_id",
                table: "verifications",
                column: "image_id",
                principalTable: "verification_images",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_verifications_verification_images_image_id",
                table: "verifications");

            migrationBuilder.DropTable(
                name: "gpay_transactions");

            migrationBuilder.DropTable(
                name: "verification_images");

            migrationBuilder.DropIndex(
                name: "ix_verifications_image_id",
                table: "verifications");

            migrationBuilder.DropIndex(
                name: "ix_verifications_one_pending_per_user",
                table: "verifications");

            migrationBuilder.DropIndex(
                name: "ix_verifications_status_created_at",
                table: "verifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_verifications_document_type",
                table: "verifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_verifications_status",
                table: "verifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_gpay_accounts_balance_not_negative",
                table: "gpay_accounts");

            migrationBuilder.DropColumn(
                name: "document_type",
                table: "verifications");

            migrationBuilder.DropColumn(
                name: "image_id",
                table: "verifications");

            migrationBuilder.DropColumn(
                name: "rejection_reason",
                table: "verifications");

            migrationBuilder.DropColumn(
                name: "reviewed_by_id",
                table: "verifications");

            migrationBuilder.DropColumn(
                name: "identity_verified",
                table: "users");
        }
    }
}
