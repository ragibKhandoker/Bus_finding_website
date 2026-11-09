using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserWhatsApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "whats_app",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "whats_app",
                table: "users");
        }
    }
}
