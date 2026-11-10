using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLostFoundMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lost_found_conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    other_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_read_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    other_read_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_message_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lost_found_conversations", x => x.id);
                    table.CheckConstraint("ck_lost_found_conversations_not_self", "owner_id <> other_user_id");
                    table.ForeignKey(
                        name: "fk_lost_found_conversations_lost_found_reports_report_id",
                        column: x => x.report_id,
                        principalTable: "lost_found_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_lost_found_conversations_users_other_user_id",
                        column: x => x.other_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_lost_found_conversations_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lost_found_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lost_found_messages", x => x.id);
                    table.CheckConstraint("ck_lost_found_messages_text", "text ~ '\\S'");
                    table.ForeignKey(
                        name: "fk_lost_found_messages_lost_found_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "lost_found_conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lost_found_conversations_other_user_id_last_message_at",
                table: "lost_found_conversations",
                columns: new[] { "other_user_id", "last_message_at" });

            migrationBuilder.CreateIndex(
                name: "ix_lost_found_conversations_owner_id_last_message_at",
                table: "lost_found_conversations",
                columns: new[] { "owner_id", "last_message_at" });

            migrationBuilder.CreateIndex(
                name: "ix_lost_found_conversations_report_id_other_user_id",
                table: "lost_found_conversations",
                columns: new[] { "report_id", "other_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lost_found_messages_conversation_id_created_at",
                table: "lost_found_messages",
                columns: new[] { "conversation_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lost_found_messages");

            migrationBuilder.DropTable(
                name: "lost_found_conversations");
        }
    }
}
