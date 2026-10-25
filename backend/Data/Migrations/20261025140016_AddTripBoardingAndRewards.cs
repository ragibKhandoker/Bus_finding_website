using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTripBoardingAndRewards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "trip_id",
                table: "wallet_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ended_at",
                table: "trips",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "gems_earned",
                table: "trips",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "individual_bus_id",
                table: "trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "trips",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                // Trips saved before boarding existed were all logged after the fact, so they are completed.
                defaultValue: "completed");

            // trips.version maps to PostgreSQL's built-in xmin system column, so there is nothing to add.

            migrationBuilder.CreateIndex(
                name: "ix_wallet_transactions_trip_id",
                table: "wallet_transactions",
                column: "trip_id",
                unique: true,
                filter: "trip_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_trips_individual_bus_id",
                table: "trips",
                column: "individual_bus_id");

            migrationBuilder.CreateIndex(
                name: "ix_trips_one_active_per_user",
                table: "trips",
                column: "user_id",
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.AddForeignKey(
                name: "fk_trips_individual_buses_individual_bus_id",
                table: "trips",
                column: "individual_bus_id",
                principalTable: "individual_buses",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_trips_individual_buses_individual_bus_id",
                table: "trips");

            migrationBuilder.DropIndex(
                name: "ix_wallet_transactions_trip_id",
                table: "wallet_transactions");

            migrationBuilder.DropIndex(
                name: "ix_trips_individual_bus_id",
                table: "trips");

            migrationBuilder.DropIndex(
                name: "ix_trips_one_active_per_user",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "trip_id",
                table: "wallet_transactions");

            migrationBuilder.DropColumn(
                name: "ended_at",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "gems_earned",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "individual_bus_id",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "status",
                table: "trips");

        }
    }
}
