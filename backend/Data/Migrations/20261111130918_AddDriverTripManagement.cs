using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDriverTripManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "individual_bus_id",
                table: "driver_bus_assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "driver_status_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    driver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    new_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    changed_by = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_driver_status_history", x => x.id);
                    table.CheckConstraint("ck_driver_status_history_changed_by", "changed_by IN ('driver', 'system', 'admin')");
                    table.ForeignKey(
                        name: "fk_driver_status_history_users_driver_id",
                        column: x => x.driver_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "driver_trips",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    driver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bus_id = table.Column<Guid>(type: "uuid", nullable: false),
                    individual_bus_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    schedule_date = table.Column<DateOnly>(type: "date", nullable: false),
                    trip_number = table.Column<int>(type: "integer", nullable: false),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    start_stop = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    end_stop = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    scheduled_departure = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    scheduled_arrival = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    actual_departure = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    actual_arrival = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    duration_minutes = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_by_admin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_admin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reminder_sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_driver_trips", x => x.id);
                    table.CheckConstraint("ck_driver_trips_actual", "actual_arrival IS NULL OR (actual_departure IS NOT NULL AND actual_arrival >= actual_departure)");
                    table.CheckConstraint("ck_driver_trips_direction", "direction IN ('FORWARD', 'RETURN')");
                    table.CheckConstraint("ck_driver_trips_duration", "duration_minutes IS NULL OR duration_minutes >= 0");
                    table.CheckConstraint("ck_driver_trips_status", "status IN ('SCHEDULED', 'IN_PROGRESS', 'COMPLETED', 'CANCELLED', 'MISSED')");
                    table.CheckConstraint("ck_driver_trips_times", "scheduled_arrival > scheduled_departure");
                    table.ForeignKey(
                        name: "fk_driver_trips_buses_bus_id",
                        column: x => x.bus_id,
                        principalTable: "buses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_driver_trips_driver_bus_assignments_assignment_id",
                        column: x => x.assignment_id,
                        principalTable: "driver_bus_assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_driver_trips_individual_buses_individual_bus_id",
                        column: x => x.individual_bus_id,
                        principalTable: "individual_buses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_driver_trips_users_driver_id",
                        column: x => x.driver_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "driver_working_statuses",
                columns: table => new
                {
                    driver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_status_changed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_driver_working_statuses", x => x.driver_id);
                    table.CheckConstraint("ck_driver_working_statuses_status", "current_status IN ('AVAILABLE', 'ON_DUTY', 'ON_TRIP', 'BREAK', 'OFF_DUTY', 'UNAVAILABLE')");
                    table.ForeignKey(
                        name: "fk_driver_working_statuses_users_driver_id",
                        column: x => x.driver_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_driver_bus_assignments_individual_bus_id",
                table: "driver_bus_assignments",
                column: "individual_bus_id");

            migrationBuilder.CreateIndex(
                name: "ix_driver_status_history_driver_id_changed_at",
                table: "driver_status_history",
                columns: new[] { "driver_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_driver_trips_assignment_id",
                table: "driver_trips",
                column: "assignment_id");

            migrationBuilder.CreateIndex(
                name: "ix_driver_trips_bus_id_schedule_date",
                table: "driver_trips",
                columns: new[] { "bus_id", "schedule_date" });

            migrationBuilder.CreateIndex(
                name: "ix_driver_trips_driver_id_schedule_date",
                table: "driver_trips",
                columns: new[] { "driver_id", "schedule_date" });

            migrationBuilder.CreateIndex(
                name: "ix_driver_trips_driver_id_status_scheduled_departure",
                table: "driver_trips",
                columns: new[] { "driver_id", "status", "scheduled_departure" });

            migrationBuilder.CreateIndex(
                name: "ix_driver_trips_individual_bus_id_scheduled_departure",
                table: "driver_trips",
                columns: new[] { "individual_bus_id", "scheduled_departure" });

            migrationBuilder.CreateIndex(
                name: "ix_driver_trips_one_in_progress",
                table: "driver_trips",
                column: "driver_id",
                unique: true,
                filter: "status = 'IN_PROGRESS'");

            migrationBuilder.CreateIndex(
                name: "ix_driver_trips_status_scheduled_departure",
                table: "driver_trips",
                columns: new[] { "status", "scheduled_departure" });

            migrationBuilder.CreateIndex(
                name: "ix_driver_working_statuses_current_status",
                table: "driver_working_statuses",
                column: "current_status");

            migrationBuilder.AddForeignKey(
                name: "fk_driver_bus_assignments_individual_buses_individual_bus_id",
                table: "driver_bus_assignments",
                column: "individual_bus_id",
                principalTable: "individual_buses",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_driver_bus_assignments_individual_buses_individual_bus_id",
                table: "driver_bus_assignments");

            migrationBuilder.DropTable(
                name: "driver_status_history");

            migrationBuilder.DropTable(
                name: "driver_trips");

            migrationBuilder.DropTable(
                name: "driver_working_statuses");

            migrationBuilder.DropIndex(
                name: "ix_driver_bus_assignments_individual_bus_id",
                table: "driver_bus_assignments");

            migrationBuilder.DropColumn(
                name: "individual_bus_id",
                table: "driver_bus_assignments");
        }
    }
}
