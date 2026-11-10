using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLostFoundChatMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_lost_found_messages_text",
                table: "lost_found_messages");

            migrationBuilder.AddColumn<string>(
                name: "image_key",
                table: "lost_found_messages",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "lost_found_message_reactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    emoji = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lost_found_message_reactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_lost_found_message_reactions_lost_found_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "lost_found_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_lost_found_messages_text",
                table: "lost_found_messages",
                sql: "text ~ '\\S' OR image_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_lost_found_message_reactions_message_id_user_id",
                table: "lost_found_message_reactions",
                columns: new[] { "message_id", "user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lost_found_message_reactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lost_found_messages_text",
                table: "lost_found_messages");

            migrationBuilder.DropColumn(
                name: "image_key",
                table: "lost_found_messages");

            migrationBuilder.AddCheckConstraint(
                name: "ck_lost_found_messages_text",
                table: "lost_found_messages",
                sql: "text ~ '\\S'");
        }
    }
}
