using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFareCalculationDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_fares_user_id",
                table: "fares");

            migrationBuilder.AddColumn<bool>(
                name: "distance_estimated",
                table: "fares",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "fare_per_km",
                table: "fares",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_fares_user_id_created_at",
                table: "fares",
                columns: new[] { "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_fares_user_id_created_at",
                table: "fares");

            migrationBuilder.DropColumn(
                name: "distance_estimated",
                table: "fares");

            migrationBuilder.DropColumn(
                name: "fare_per_km",
                table: "fares");

            migrationBuilder.CreateIndex(
                name: "ix_fares_user_id",
                table: "fares",
                column: "user_id");
        }
    }
}
