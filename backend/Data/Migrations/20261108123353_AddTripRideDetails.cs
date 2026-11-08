using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTripRideDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "base_fare",
                table: "trips",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bus_code",
                table: "trips",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bus_type",
                table: "trips",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "operator",
                table: "trips",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "per_km_fare",
                table: "trips",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "route_number",
                table: "trips",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "stops_ridden",
                table: "trips",
                type: "text[]",
                nullable: true);

            // Fill in the new details for trips already in history, from their route and vehicle where those still
            // exist. Only the new, empty columns are written; nothing that was already saved changes.
            migrationBuilder.Sql("""
                UPDATE trips t
                SET route_number = b.route_number, bus_type = b.bus_type, operator = b.operator,
                    base_fare = b.base_fare, per_km_fare = b.per_km_fare
                FROM buses b
                WHERE t.bus_id = b.id::text AND t.individual_bus_id IS NOT NULL;
                """);
            migrationBuilder.Sql("""
                UPDATE trips t
                SET bus_code = v.bus_code
                FROM individual_buses v
                WHERE t.individual_bus_id = v.id;
                """);
            // The stops ridden, for completed trips whose start and end stops are still on the route.
            migrationBuilder.Sql("""
                UPDATE trips t
                SET stops_ridden = s.names
                FROM (
                    SELECT t2.id,
                        (SELECT array_agg(bs.name ORDER BY CASE WHEN f.ord < e.ord THEN bs."order" ELSE -bs."order" END)
                         FROM bus_stops bs
                         WHERE bs.bus_id = b.id AND bs."order" BETWEEN LEAST(f.ord, e.ord) AND GREATEST(f.ord, e.ord)) AS names
                    FROM trips t2
                    JOIN buses b ON t2.bus_id = b.id::text
                    CROSS JOIN LATERAL (SELECT MIN(bs."order") AS ord FROM bus_stops bs
                        WHERE bs.bus_id = b.id AND lower(trim(bs.name)) = lower(trim(t2.source))) f
                    CROSS JOIN LATERAL (SELECT MIN(bs."order") AS ord FROM bus_stops bs
                        WHERE bs.bus_id = b.id AND lower(trim(bs.name)) = lower(trim(t2.destination))) e
                    WHERE t2.status = 'completed' AND t2.individual_bus_id IS NOT NULL
                      AND f.ord IS NOT NULL AND e.ord IS NOT NULL AND f.ord <> e.ord
                ) s
                WHERE t.id = s.id AND s.names IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "base_fare",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "bus_code",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "bus_type",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "operator",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "per_km_fare",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "route_number",
                table: "trips");

            migrationBuilder.DropColumn(
                name: "stops_ridden",
                table: "trips");
        }
    }
}
