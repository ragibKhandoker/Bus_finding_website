using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    // Notifications & SOS (F14): a category (what the notification is about), the sender and the related item.
    // Existing notifications are given a category from their type and title; nothing is deleted.
    /// <inheritdoc />
    public partial class AddNotificationDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "notifications",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "general");

            migrationBuilder.AddColumn<Guid>(
                name: "related_id",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "sender_id",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(@"
UPDATE notifications SET category = CASE
    WHEN type = 'sos' THEN 'sos'
    WHEN title ILIKE '%friend%' THEN 'friend'
    WHEN title ILIKE '%ride%' THEN 'ride'
    WHEN title ILIKE '%gem%' OR title ILIKE '%reward%' OR title ILIKE '%cashback%' THEN 'reward'
    WHEN title ILIKE '%profile%' OR title ILIKE '%password%' OR title ILIKE '%verification%' THEN 'account'
    ELSE 'general' END;
");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_sender_id",
                table: "notifications",
                column: "sender_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_created_at",
                table: "notifications",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_notifications_category",
                table: "notifications",
                sql: "category IN ('general', 'account', 'friend', 'ride', 'reward', 'sos')");

            migrationBuilder.AddForeignKey(
                name: "fk_notifications_users_sender_id",
                table: "notifications",
                column: "sender_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_notifications_users_sender_id",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_notifications_sender_id",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_notifications_user_id_created_at",
                table: "notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notifications_category",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "category",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "related_id",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "sender_id",
                table: "notifications");
        }
    }
}
