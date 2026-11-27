using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRouteSuggestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "route_suggestions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    related_bus_id = table.Column<Guid>(type: "uuid", nullable: true),
                    related_bus_stop_id = table.Column<Guid>(type: "uuid", nullable: true),
                    related_bus_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    related_route_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    related_stop_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    route_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    route_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    bus_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    bus_stop_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    location_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    correction_field = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    corrected_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    image_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    image_content_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    admin_review_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    reviewed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    applied_change = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    applied_bus_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_route_suggestions", x => x.id);
                    table.CheckConstraint("ck_route_suggestions_coordinates", "(latitude IS NULL) = (longitude IS NULL) AND (latitude IS NULL OR (latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180))");
                    table.CheckConstraint("ck_route_suggestions_reviewed", "status = 'PENDING' OR reviewed_at IS NOT NULL");
                    table.CheckConstraint("ck_route_suggestions_status", "status IN ('PENDING', 'APPROVED', 'REJECTED')");
                    table.CheckConstraint("ck_route_suggestions_text", "title ~ '\\S' AND description ~ '\\S'");
                    table.CheckConstraint("ck_route_suggestions_type", "type IN ('NEW_ROUTE', 'NEW_STOP', 'INCORRECT_INFORMATION')");
                    table.ForeignKey(
                        name: "fk_route_suggestions_bus_stops_related_bus_stop_id",
                        column: x => x.related_bus_stop_id,
                        principalTable: "bus_stops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_route_suggestions_buses_related_bus_id",
                        column: x => x.related_bus_id,
                        principalTable: "buses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_route_suggestions_users_reviewed_by_id",
                        column: x => x.reviewed_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_route_suggestions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "route_suggestion_stops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    suggestion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    lat = table.Column<double>(type: "double precision", nullable: false),
                    lng = table.Column<double>(type: "double precision", nullable: false),
                    is_existing = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_route_suggestion_stops", x => x.id);
                    table.ForeignKey(
                        name: "fk_route_suggestion_stops_route_suggestions_suggestion_id",
                        column: x => x.suggestion_id,
                        principalTable: "route_suggestions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_route_suggestion_stops_suggestion_id_order",
                table: "route_suggestion_stops",
                columns: new[] { "suggestion_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_route_suggestions_related_bus_id",
                table: "route_suggestions",
                column: "related_bus_id");

            migrationBuilder.CreateIndex(
                name: "ix_route_suggestions_related_bus_stop_id",
                table: "route_suggestions",
                column: "related_bus_stop_id");

            migrationBuilder.CreateIndex(
                name: "ix_route_suggestions_reviewed_by_id",
                table: "route_suggestions",
                column: "reviewed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_route_suggestions_status_created_at",
                table: "route_suggestions",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_route_suggestions_user_id_created_at",
                table: "route_suggestions",
                columns: new[] { "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "route_suggestion_stops");

            migrationBuilder.DropTable(
                name: "route_suggestions");
        }
    }
}
