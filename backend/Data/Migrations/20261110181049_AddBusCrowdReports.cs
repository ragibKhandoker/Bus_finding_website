using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBusCrowdReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bus_crowd_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bus_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bus_stop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bus_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    route_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    route_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    stop_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    travel_direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    towards_stop = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    crowd_level = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    seat_availability = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    available_seat_count = table.Column<int>(type: "integer", nullable: true),
                    standing_passenger_count = table.Column<int>(type: "integer", nullable: true),
                    observation_date_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    observation_slot = table.Column<long>(type: "bigint", nullable: false),
                    additional_comments = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bus_crowd_reports", x => x.id);
                    table.CheckConstraint("ck_bus_crowd_reports_crowd_level", "crowd_level IN ('LOW', 'MEDIUM', 'HIGH')");
                    table.CheckConstraint("ck_bus_crowd_reports_direction", "travel_direction IN ('FORWARD', 'RETURN')");
                    table.CheckConstraint("ck_bus_crowd_reports_no_seats", "seat_availability <> 'NO_SEATS' OR available_seat_count IS NULL OR available_seat_count = 0");
                    table.CheckConstraint("ck_bus_crowd_reports_seat_availability", "seat_availability IN ('AVAILABLE', 'LIMITED', 'NO_SEATS')");
                    table.CheckConstraint("ck_bus_crowd_reports_seat_count", "available_seat_count IS NULL OR available_seat_count BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_bus_crowd_reports_standing_count", "standing_passenger_count IS NULL OR standing_passenger_count BETWEEN 0 AND 200");
                    table.ForeignKey(
                        name: "fk_bus_crowd_reports_bus_stops_bus_stop_id",
                        column: x => x.bus_stop_id,
                        principalTable: "bus_stops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bus_crowd_reports_buses_bus_id",
                        column: x => x.bus_id,
                        principalTable: "buses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bus_crowd_reports_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bus_crowd_reports_bus_id_observation_date_time",
                table: "bus_crowd_reports",
                columns: new[] { "bus_id", "observation_date_time" });

            migrationBuilder.CreateIndex(
                name: "ix_bus_crowd_reports_bus_name",
                table: "bus_crowd_reports",
                column: "bus_name");

            migrationBuilder.CreateIndex(
                name: "ix_bus_crowd_reports_bus_stop_id_observation_date_time",
                table: "bus_crowd_reports",
                columns: new[] { "bus_stop_id", "observation_date_time" });

            migrationBuilder.CreateIndex(
                name: "ix_bus_crowd_reports_observation_date_time",
                table: "bus_crowd_reports",
                column: "observation_date_time");

            migrationBuilder.CreateIndex(
                name: "ix_bus_crowd_reports_one_per_sighting",
                table: "bus_crowd_reports",
                columns: new[] { "user_id", "bus_id", "bus_stop_id", "observation_slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bus_crowd_reports_user_id_created_at",
                table: "bus_crowd_reports",
                columns: new[] { "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bus_crowd_reports");
        }
    }
}
