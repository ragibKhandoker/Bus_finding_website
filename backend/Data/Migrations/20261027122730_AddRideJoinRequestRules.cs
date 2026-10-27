using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    // Ride join requests (F12): updated_at, a status check, and at most one live (pending or accepted) request per
    // rider per ride. Existing rows are tidied first so the unique index can be built: updated_at = created_at, an
    // unknown status or a host's request for their own ride becomes rejected, and where a rider has several live
    // requests for one ride the accepted one (or else the oldest pending one) is kept and the rest become rejected.
    /// <inheritdoc />
    public partial class AddRideJoinRequestRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ride_requests_requester_id",
                table: "ride_requests");

            migrationBuilder.DropIndex(
                name: "ix_ride_requests_ride_post_id_requester_id",
                table: "ride_requests");

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "ride_requests",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.Sql(@"
UPDATE ride_requests SET updated_at = created_at;
UPDATE ride_requests SET status = 'rejected' WHERE status NOT IN ('pending', 'accepted', 'rejected');
UPDATE ride_requests r SET status = 'rejected' FROM ride_posts p WHERE p.id = r.ride_post_id AND p.user_id = r.requester_id;
UPDATE ride_requests r SET status = 'rejected'
  FROM (SELECT id, row_number() OVER (
               PARTITION BY ride_post_id, requester_id
               ORDER BY (status = 'accepted') DESC, created_at, id) AS n
          FROM ride_requests WHERE status <> 'rejected') d
 WHERE r.id = d.id AND d.n > 1;
");

            migrationBuilder.CreateIndex(
                name: "ix_ride_requests_one_live_per_rider",
                table: "ride_requests",
                columns: new[] { "ride_post_id", "requester_id" },
                unique: true,
                filter: "status <> 'rejected'");

            migrationBuilder.CreateIndex(
                name: "ix_ride_requests_requester_id_created_at",
                table: "ride_requests",
                columns: new[] { "requester_id", "created_at" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_ride_requests_status",
                table: "ride_requests",
                sql: "status IN ('pending', 'accepted', 'rejected')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ride_requests_one_live_per_rider",
                table: "ride_requests");

            migrationBuilder.DropIndex(
                name: "ix_ride_requests_requester_id_created_at",
                table: "ride_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ride_requests_status",
                table: "ride_requests");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "ride_requests");

            migrationBuilder.CreateIndex(
                name: "ix_ride_requests_requester_id",
                table: "ride_requests",
                column: "requester_id");

            migrationBuilder.CreateIndex(
                name: "ix_ride_requests_ride_post_id_requester_id",
                table: "ride_requests",
                columns: new[] { "ride_post_id", "requester_id" });
        }
    }
}
