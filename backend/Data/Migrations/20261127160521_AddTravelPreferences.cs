using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTravelPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "travel_preferences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    preference_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    start_location = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    destination = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    bus_id = table.Column<Guid>(type: "uuid", nullable: true),
                    frequently_used_bus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    bus_route_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    preferred_travel_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    weekly_frequency = table.Column<int>(type: "integer", nullable: false),
                    travel_days = table.Column<List<string>>(type: "text[]", nullable: false),
                    bus_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    travel_purpose = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_travel_preferences", x => x.id);
                    table.CheckConstraint("ck_travel_preferences_bus_type", "bus_type IN ('AC', 'Non-AC', 'Both')");
                    table.CheckConstraint("ck_travel_preferences_purpose", "travel_purpose IN ('University', 'Office', 'Coaching', 'Shopping', 'Medical', 'Personal', 'Other')");
                    table.CheckConstraint("ck_travel_preferences_text", "preference_name ~ '\\S' AND start_location ~ '\\S' AND destination ~ '\\S'");
                    table.CheckConstraint("ck_travel_preferences_travel_days", "cardinality(travel_days) BETWEEN 1 AND 7");
                    table.CheckConstraint("ck_travel_preferences_weekly_frequency", "weekly_frequency BETWEEN 1 AND 7");
                    table.ForeignKey(
                        name: "fk_travel_preferences_buses_bus_id",
                        column: x => x.bus_id,
                        principalTable: "buses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_travel_preferences_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_travel_preferences_bus_id",
                table: "travel_preferences",
                column: "bus_id");

            migrationBuilder.CreateIndex(
                name: "ix_travel_preferences_user_id_created_at",
                table: "travel_preferences",
                columns: new[] { "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "travel_preferences");
        }
    }
}
