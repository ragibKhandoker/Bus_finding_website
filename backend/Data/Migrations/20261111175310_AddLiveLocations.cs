using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "live_locations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    driver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bus_id = table.Column<Guid>(type: "uuid", nullable: false),
                    individual_bus_id = table.Column<Guid>(type: "uuid", nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    accuracy = table.Column<double>(type: "double precision", nullable: true),
                    speed = table.Column<double>(type: "double precision", nullable: true),
                    heading = table.Column<double>(type: "double precision", nullable: true),
                    is_sharing = table.Column<bool>(type: "boolean", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    stopped_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    stop_reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    update_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_locations", x => x.id);
                    table.CheckConstraint("ck_live_locations_heading", "heading IS NULL OR heading BETWEEN 0 AND 360");
                    table.CheckConstraint("ck_live_locations_latitude", "latitude IS NULL OR latitude BETWEEN -90 AND 90");
                    table.CheckConstraint("ck_live_locations_longitude", "longitude IS NULL OR longitude BETWEEN -180 AND 180");
                    table.ForeignKey(
                        name: "fk_live_locations_buses_bus_id",
                        column: x => x.bus_id,
                        principalTable: "buses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_live_locations_driver_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "driver_trips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_live_locations_individual_buses_individual_bus_id",
                        column: x => x.individual_bus_id,
                        principalTable: "individual_buses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_live_locations_users_driver_id",
                        column: x => x.driver_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_live_locations_bus_id_is_sharing",
                table: "live_locations",
                columns: new[] { "bus_id", "is_sharing" });

            migrationBuilder.CreateIndex(
                name: "ix_live_locations_driver_id_is_sharing",
                table: "live_locations",
                columns: new[] { "driver_id", "is_sharing" });

            migrationBuilder.CreateIndex(
                name: "ix_live_locations_individual_bus_id",
                table: "live_locations",
                column: "individual_bus_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_locations_is_sharing_last_updated_at",
                table: "live_locations",
                columns: new[] { "is_sharing", "last_updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_live_locations_trip_id",
                table: "live_locations",
                column: "trip_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "live_locations");
        }
    }
}
