using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdvancedRideSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ride_requests_one_live_per_rider",
                table: "ride_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ride_requests_status",
                table: "ride_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ride_posts_places_not_blank",
                table: "ride_posts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ride_posts_status",
                table: "ride_posts");

            migrationBuilder.AddColumn<string>(
                name: "note",
                table: "ride_requests",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "removed_by_host",
                table: "ride_requests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "cancel_reason",
                table: "ride_posts",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "cancelled_at",
                table: "ride_posts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_preference",
                table: "ride_posts",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                // Rides posted before contact preferences existed: in-app chat only.
                defaultValue: "chat");

            migrationBuilder.AddColumn<decimal>(
                name: "cost_per_person",
                table: "ride_posts",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "departure_at",
                table: "ride_posts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "destination_lat",
                table: "ride_posts",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "destination_lng",
                table: "ride_posts",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "expired_at",
                table: "ride_posts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "instructions",
                table: "ride_posts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pickup_details",
                table: "ride_posts",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reminder_sent_at",
                table: "ride_posts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "source_lat",
                table: "ride_posts",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "source_lng",
                table: "ride_posts",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "vehicle_type",
                table: "ride_posts",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                // Rides posted before vehicles were asked.
                defaultValue: "Other");

            migrationBuilder.CreateTable(
                name: "ride_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ride_post_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ride_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_ride_messages_ride_posts_ride_post_id",
                        column: x => x.ride_post_id,
                        principalTable: "ride_posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ride_messages_users_sender_id",
                        column: x => x.sender_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ride_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ride_post_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reporter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    admin_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reviewed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ride_reports", x => x.id);
                    table.CheckConstraint("ck_ride_reports_status", "status IN ('open', 'reviewed', 'dismissed')");
                    table.CheckConstraint("ck_ride_reports_target", "ride_post_id IS NOT NULL OR reported_user_id IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_ride_reports_ride_posts_ride_post_id",
                        column: x => x.ride_post_id,
                        principalTable: "ride_posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_ride_reports_users_reported_user_id",
                        column: x => x.reported_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_ride_reports_users_reporter_id",
                        column: x => x.reporter_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ride_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ride_post_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewed_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ride_reviews", x => x.id);
                    table.CheckConstraint("ck_ride_reviews_not_self", "reviewer_id <> reviewed_user_id");
                    table.CheckConstraint("ck_ride_reviews_rating", "rating BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_ride_reviews_ride_posts_ride_post_id",
                        column: x => x.ride_post_id,
                        principalTable: "ride_posts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ride_reviews_users_reviewed_user_id",
                        column: x => x.reviewed_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ride_reviews_users_reviewer_id",
                        column: x => x.reviewer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_blocks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    blocker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blocked_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_blocks", x => x.id);
                    table.CheckConstraint("ck_user_blocks_not_self", "blocker_id <> blocked_id");
                    table.ForeignKey(
                        name: "fk_user_blocks_users_blocked_id",
                        column: x => x.blocked_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_blocks_users_blocker_id",
                        column: x => x.blocker_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ride_requests_one_live_per_rider",
                table: "ride_requests",
                columns: new[] { "ride_post_id", "requester_id" },
                unique: true,
                filter: "status IN ('pending', 'accepted')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ride_requests_status",
                table: "ride_requests",
                sql: "status IN ('pending', 'accepted', 'rejected', 'cancelled')");

            migrationBuilder.CreateIndex(
                name: "ix_ride_posts_live_departure",
                table: "ride_posts",
                column: "departure_at",
                filter: "status IN ('active', 'full') AND deleted_at IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ride_posts_contact_preference",
                table: "ride_posts",
                sql: "contact_preference IN ('chat', 'phone', 'both')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ride_posts_cost",
                table: "ride_posts",
                sql: "cost_per_person IS NULL OR cost_per_person >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ride_posts_status",
                table: "ride_posts",
                sql: "status IN ('active', 'full', 'completed', 'cancelled', 'expired')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ride_posts_vehicle_type",
                table: "ride_posts",
                sql: "vehicle_type IN ('Car', 'Bike', 'CNG', 'Other')");

            migrationBuilder.CreateIndex(
                name: "ix_ride_messages_ride_post_id_created_at",
                table: "ride_messages",
                columns: new[] { "ride_post_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ride_messages_sender_id",
                table: "ride_messages",
                column: "sender_id");

            migrationBuilder.CreateIndex(
                name: "ix_ride_reports_reported_user_id",
                table: "ride_reports",
                column: "reported_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ride_reports_reporter_id_created_at",
                table: "ride_reports",
                columns: new[] { "reporter_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ride_reports_ride_post_id",
                table: "ride_reports",
                column: "ride_post_id");

            migrationBuilder.CreateIndex(
                name: "ix_ride_reports_status_created_at",
                table: "ride_reports",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ride_reviews_reviewed_user_id_created_at",
                table: "ride_reviews",
                columns: new[] { "reviewed_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ride_reviews_reviewer_id",
                table: "ride_reviews",
                column: "reviewer_id");

            migrationBuilder.CreateIndex(
                name: "ix_ride_reviews_ride_post_id_reviewer_id_reviewed_user_id",
                table: "ride_reviews",
                columns: new[] { "ride_post_id", "reviewer_id", "reviewed_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_blocks_blocked_id",
                table: "user_blocks",
                column: "blocked_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_blocks_blocker_id_blocked_id",
                table: "user_blocks",
                columns: new[] { "blocker_id", "blocked_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ride_messages");

            migrationBuilder.DropTable(
                name: "ride_reports");

            migrationBuilder.DropTable(
                name: "ride_reviews");

            migrationBuilder.DropTable(
                name: "user_blocks");

            migrationBuilder.DropIndex(
                name: "ix_ride_requests_one_live_per_rider",
                table: "ride_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ride_requests_status",
                table: "ride_requests");

            migrationBuilder.DropIndex(
                name: "ix_ride_posts_live_departure",
                table: "ride_posts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ride_posts_contact_preference",
                table: "ride_posts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ride_posts_cost",
                table: "ride_posts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ride_posts_status",
                table: "ride_posts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ride_posts_vehicle_type",
                table: "ride_posts");

            migrationBuilder.DropColumn(
                name: "note",
                table: "ride_requests");

            migrationBuilder.DropColumn(
                name: "removed_by_host",
                table: "ride_requests");

            migrationBuilder.DropColumn(
                name: "cancel_reason",
                table: "ride_posts");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                table: "ride_posts");

            migrationBuilder.DropColumn(
                name: "contact_preference",
                table: "ride_posts");

            migrationBuilder.DropColumn(
                name: "cost_per_person",
                table: "ride_posts");

            migrationBuilder.DropColumn(
                name: "departure_at",
                table: "ride_posts");

            migrationBuilder.DropColumn(
                name: "destination_lat",
                table: "ride_posts");

            migrationBuilder.DropColumn(
                name: "destination_lng",
                table: "ride_posts");

            migrationBuilder.DropColumn(
                name: "expired_at",
                table: "ride_posts");

            migrationBuilder.DropColumn(
                name: "instructions",
                table: "ride_posts");

            migrationBuilder.DropColumn(
                name: "pickup_details",
                table: "ride_posts");

            migrationBuilder.DropColumn(
                name: "reminder_sent_at",
                table: "ride_posts");

            migrationBuilder.DropColumn(
                name: "source_lat",
                table: "ride_posts");

            migrationBuilder.DropColumn(
                name: "source_lng",
                table: "ride_posts");

            migrationBuilder.DropColumn(
                name: "vehicle_type",
                table: "ride_posts");

            migrationBuilder.CreateIndex(
                name: "ix_ride_requests_one_live_per_rider",
                table: "ride_requests",
                columns: new[] { "ride_post_id", "requester_id" },
                unique: true,
                filter: "status <> 'rejected'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ride_requests_status",
                table: "ride_requests",
                sql: "status IN ('pending', 'accepted', 'rejected')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ride_posts_places_not_blank",
                table: "ride_posts",
                sql: "source ~ '\\S' AND destination ~ '\\S'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ride_posts_status",
                table: "ride_posts",
                sql: "status IN ('active', 'completed')");
        }
    }
}
