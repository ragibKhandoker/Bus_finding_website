using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBusQrBoarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "boarding_lat",
                table: "trips",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "boarding_lng",
                table: "trips",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "boarding_method",
                table: "trips",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "end_lat",
                table: "trips",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "end_lng",
                table: "trips",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bus_qr_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    individual_bus_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    token_cipher = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deactivated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deactivated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scan_count = table.Column<int>(type: "integer", nullable: false),
                    last_scanned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bus_qr_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_bus_qr_codes_individual_buses_individual_bus_id",
                        column: x => x.individual_bus_id,
                        principalTable: "individual_buses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bus_qr_codes_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "bus_qr_scans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    qr_code_id = table.Column<Guid>(type: "uuid", nullable: false),
                    individual_bus_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bus_qr_scans", x => x.id);
                    table.ForeignKey(
                        name: "fk_bus_qr_scans_bus_qr_codes_qr_code_id",
                        column: x => x.qr_code_id,
                        principalTable: "bus_qr_codes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bus_qr_scans_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bus_qr_codes_created_by_id",
                table: "bus_qr_codes",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_bus_qr_codes_one_active_per_bus",
                table: "bus_qr_codes",
                column: "individual_bus_id",
                unique: true,
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_bus_qr_codes_token_hash",
                table: "bus_qr_codes",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bus_qr_scans_qr_code_id",
                table: "bus_qr_scans",
                column: "qr_code_id");

            migrationBuilder.CreateIndex(
                name: "ix_bus_qr_scans_ticket_hash",
                table: "bus_qr_scans",
                column: "ticket_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bus_qr_scans_user_id_created_at",
                table: "bus_qr_scans",
                columns: new[] { "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bus_qr_scans");

            migrationBuilder.DropTable(
                name: "bus_qr_codes");

            migrationBuilder.DropColumn(
                name: "boarding_lat",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "boarding_lng",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "boarding_method",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "end_lat",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "end_lng",
                table: "trips");
        }
    }
}
