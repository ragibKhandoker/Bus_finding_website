using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartFavouriteBusModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_notifications_category",
                table: "notifications");

            migrationBuilder.CreateTable(
                name: "bus_availability_alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bus_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_stop = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    to_stop = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_seats_free = table.Column<int>(type: "integer", nullable: true),
                    last_notified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bus_availability_alerts", x => x.id);
                    table.ForeignKey(
                        name: "fk_bus_availability_alerts_buses_bus_id",
                        column: x => x.bus_id,
                        principalTable: "buses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bus_availability_alerts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bus_fare_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bus_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_fare = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    per_km_fare = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    previous_base_fare = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    previous_per_km_fare = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    recorded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bus_fare_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_bus_fare_history_buses_bus_id",
                        column: x => x.bus_id,
                        principalTable: "buses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bus_reminders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bus_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stop_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    departure_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    lead_minutes = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bus_reminders", x => x.id);
                    table.CheckConstraint("ck_bus_reminders_lead_minutes", "lead_minutes IN (15, 30, 60)");
                    table.ForeignKey(
                        name: "fk_bus_reminders_buses_bus_id",
                        column: x => x.bus_id,
                        principalTable: "buses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bus_reminders_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bus_watch_states",
                columns: table => new
                {
                    bus_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_fare = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    per_km_fare = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    stop_names = table.Column<List<string>>(type: "text[]", nullable: false),
                    active_vehicles = table.Column<int>(type: "integer", nullable: false),
                    checked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bus_watch_states", x => x.bus_id);
                    table.ForeignKey(
                        name: "fk_bus_watch_states_buses_bus_id",
                        column: x => x.bus_id,
                        principalTable: "buses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "favorite_collections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_favorite_collections", x => x.id);
                    table.CheckConstraint("ck_favorite_collections_name", "length(btrim(name)) > 0");
                    table.ForeignKey(
                        name: "fk_favorite_collections_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recently_viewed_buses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bus_id = table.Column<Guid>(type: "uuid", nullable: false),
                    viewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recently_viewed_buses", x => x.id);
                    table.ForeignKey(
                        name: "fk_recently_viewed_buses_buses_bus_id",
                        column: x => x.bus_id,
                        principalTable: "buses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_recently_viewed_buses_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "favorite_collection_buses",
                columns: table => new
                {
                    collection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    favorite_bus_id = table.Column<Guid>(type: "uuid", nullable: false),
                    added_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_favorite_collection_buses", x => new { x.collection_id, x.favorite_bus_id });
                    table.ForeignKey(
                        name: "fk_favorite_collection_buses_favorite_buses_favorite_bus_id",
                        column: x => x.favorite_bus_id,
                        principalTable: "favorite_buses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_favorite_collection_buses_favorite_collections_collection_id",
                        column: x => x.collection_id,
                        principalTable: "favorite_collections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_notifications_category",
                table: "notifications",
                sql: "category IN ('general', 'account', 'friend', 'ride', 'reward', 'sos', 'bus-favourite', 'bus-fare', 'bus-availability', 'bus-reminder', 'bus-route')");

            migrationBuilder.CreateIndex(
                name: "ix_bus_availability_alerts_bus_id",
                table: "bus_availability_alerts",
                column: "bus_id");

            migrationBuilder.CreateIndex(
                name: "ix_bus_availability_alerts_is_active",
                table: "bus_availability_alerts",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_bus_availability_alerts_user_id_bus_id_from_stop_to_stop",
                table: "bus_availability_alerts",
                columns: new[] { "user_id", "bus_id", "from_stop", "to_stop" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bus_fare_history_bus_id_recorded_at",
                table: "bus_fare_history",
                columns: new[] { "bus_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bus_reminders_bus_id",
                table: "bus_reminders",
                column: "bus_id");

            migrationBuilder.CreateIndex(
                name: "ix_bus_reminders_due",
                table: "bus_reminders",
                column: "departure_at",
                filter: "is_enabled AND sent_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bus_reminders_user_id_departure_at",
                table: "bus_reminders",
                columns: new[] { "user_id", "departure_at" });

            migrationBuilder.CreateIndex(
                name: "ix_favorite_collection_buses_favorite_bus_id",
                table: "favorite_collection_buses",
                column: "favorite_bus_id");

            migrationBuilder.CreateIndex(
                name: "ix_favorite_collections_user_id_name",
                table: "favorite_collections",
                columns: new[] { "user_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recently_viewed_buses_bus_id",
                table: "recently_viewed_buses",
                column: "bus_id");

            migrationBuilder.CreateIndex(
                name: "ix_recently_viewed_buses_user_id_bus_id",
                table: "recently_viewed_buses",
                columns: new[] { "user_id", "bus_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recently_viewed_buses_user_id_viewed_at",
                table: "recently_viewed_buses",
                columns: new[] { "user_id", "viewed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bus_availability_alerts");

            migrationBuilder.DropTable(
                name: "bus_fare_history");

            migrationBuilder.DropTable(
                name: "bus_reminders");

            migrationBuilder.DropTable(
                name: "bus_watch_states");

            migrationBuilder.DropTable(
                name: "favorite_collection_buses");

            migrationBuilder.DropTable(
                name: "recently_viewed_buses");

            migrationBuilder.DropTable(
                name: "favorite_collections");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notifications_category",
                table: "notifications");

            // The module's notifications cannot stay once their categories are gone again.
            migrationBuilder.Sql("DELETE FROM notifications WHERE category LIKE 'bus-%';");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notifications_category",
                table: "notifications",
                sql: "category IN ('general', 'account', 'friend', 'ride', 'reward', 'sos')");
        }
    }
}
