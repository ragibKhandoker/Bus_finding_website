using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    // Ride share (F11): a gender preference, the updated / completed / deleted times, an index for the open-rides
    // list and one for a host's posts, and checks on status, participant count, gender preference and blank places.
    // Existing posts become "Any" with updated_at = created_at (completed ones also get completed_at), blank places
    // become "Not specified", and participant counts are pulled into the 2-20 range, so the new checks hold.
    /// <inheritdoc />
    public partial class AddRideShareLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ride_posts_user_id",
                table: "ride_posts");

            migrationBuilder.AddColumn<DateTime>(
                name: "completed_at",
                table: "ride_posts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "ride_posts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gender_preference",
                table: "ride_posts",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Any");

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "ride_posts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.Sql(@"
UPDATE ride_posts SET updated_at = created_at;
UPDATE ride_posts SET completed_at = created_at WHERE status = 'completed';
UPDATE ride_posts SET status = 'completed', completed_at = now() WHERE status NOT IN ('active', 'completed');
UPDATE ride_posts SET source = 'Not specified' WHERE source !~ '\S';
UPDATE ride_posts SET destination = 'Not specified' WHERE destination !~ '\S';
UPDATE ride_posts SET max_participants = LEAST(GREATEST(max_participants, 2), 20);
");

            migrationBuilder.CreateIndex(
                name: "ix_ride_posts_open",
                table: "ride_posts",
                column: "created_at",
                filter: "status = 'active' AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ride_posts_user_id_created_at",
                table: "ride_posts",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_ride_posts_gender_preference",
                table: "ride_posts",
                sql: "gender_preference IN ('Any', 'Male', 'Female')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ride_posts_max_participants",
                table: "ride_posts",
                sql: "max_participants BETWEEN 2 AND 20");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ride_posts_places_not_blank",
                table: "ride_posts",
                sql: "source ~ '\\S' AND destination ~ '\\S'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ride_posts_status",
                table: "ride_posts",
                sql: "status IN ('active', 'completed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ride_posts_open",
                table: "ride_posts");

            migrationBuilder.DropIndex(
                name: "ix_ride_posts_user_id_created_at",
                table: "ride_posts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ride_posts_gender_preference",
                table: "ride_posts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ride_posts_max_participants",
                table: "ride_posts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ride_posts_places_not_blank",
                table: "ride_posts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ride_posts_status",
                table: "ride_posts");

            // Before this migration deleting a post removed it, so posts deleted since are removed now rather than
            // reappearing once deleted_at is gone.
            migrationBuilder.Sql("DELETE FROM ride_posts WHERE deleted_at IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "completed_at",
                table: "ride_posts");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "ride_posts");

            migrationBuilder.DropColumn(
                name: "gender_preference",
                table: "ride_posts");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "ride_posts");

            migrationBuilder.CreateIndex(
                name: "ix_ride_posts_user_id",
                table: "ride_posts",
                column: "user_id");
        }
    }
}
