using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    // Offers (F15): the offer_transactions ledger (every grant, redemption and trip use, with unique indexes against
    // repeats) and checks that offer balances never go below zero. Existing balances are kept: any negative value is
    // set to 0 first, and each account's current balances are written to the ledger as "opening" entries.
    /// <inheritdoc />
    public partial class AddOfferLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "offer_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    wallet_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offer_transactions", x => x.id);
                    table.CheckConstraint("ck_offer_transactions_kind", "kind IN ('cashback', 'coupon', 'discount')");
                    table.CheckConstraint("ck_offer_transactions_reason", "reason IN ('welcome', 'opening', 'granted', 'adjusted', 'redeemed', 'trip')");
                    table.ForeignKey(
                        name: "fk_offer_transactions_offers_offer_id",
                        column: x => x.offer_id,
                        principalTable: "offers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_offer_transactions_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_offer_transactions_wallet_transactions_wallet_transaction_id",
                        column: x => x.wallet_transaction_id,
                        principalTable: "wallet_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.Sql(@"
UPDATE offers SET cashback = GREATEST(cashback, 0), coupon = GREATEST(coupon, 0), discount = GREATEST(discount, 0)
 WHERE cashback < 0 OR coupon < 0 OR discount < 0;
INSERT INTO offer_transactions (id, offer_id, user_id, kind, amount, balance_after, reason, description, reference, created_at)
SELECT gen_random_uuid(), o.id, o.user_id, k.kind, k.amount, k.amount, 'opening', 'Balance before the offer history started', 'opening:' || k.kind, now()
  FROM offers o
 CROSS JOIN LATERAL (VALUES ('cashback', o.cashback), ('coupon', o.coupon), ('discount', o.discount)) AS k(kind, amount)
 WHERE k.amount > 0;
");

            migrationBuilder.AddCheckConstraint(
                name: "ck_offers_cashback_not_negative",
                table: "offers",
                sql: "cashback >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_offers_coupon_not_negative",
                table: "offers",
                sql: "coupon >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_offers_discount_not_negative",
                table: "offers",
                sql: "discount >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_offer_transactions_offer_id",
                table: "offer_transactions",
                column: "offer_id");

            migrationBuilder.CreateIndex(
                name: "ix_offer_transactions_one_kind_per_trip",
                table: "offer_transactions",
                columns: new[] { "trip_id", "kind" },
                unique: true,
                filter: "trip_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_offer_transactions_one_per_reference",
                table: "offer_transactions",
                columns: new[] { "user_id", "reference" },
                unique: true,
                filter: "reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_offer_transactions_user_id_created_at",
                table: "offer_transactions",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_offer_transactions_wallet_transaction_id",
                table: "offer_transactions",
                column: "wallet_transaction_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "offer_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_offers_cashback_not_negative",
                table: "offers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_offers_coupon_not_negative",
                table: "offers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_offers_discount_not_negative",
                table: "offers");
        }
    }
}
