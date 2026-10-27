using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    // Friend requests (F09):
    //  - friendships go from two rows per pair, (A, B) and (B, A), to one row with the smaller id first;
    //  - friend requests get updated_at, cannot be sent to oneself, and at most one per pair may be pending;
    //  - every user has a well-formed friend code (any missing or malformed one is regenerated first).
    // Existing data is converted in place; no user, friendship or pending request is lost. Down restores the old shape.
    /// <inheritdoc />
    public partial class AddFriendRequestFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_friendships_users_friend_id",
                table: "friendships");

            migrationBuilder.DropForeignKey(
                name: "fk_friendships_users_user_id",
                table: "friendships");

            migrationBuilder.DropIndex(
                name: "ix_friend_requests_from_user_id",
                table: "friend_requests");

            // Keep one row per pair, with the smaller id in user_id. uuid values compare byte by byte, which is
            // the same order Friendship.Order uses in the application.
            migrationBuilder.Sql(@"
DELETE FROM friendships WHERE user_id = friend_id;
DELETE FROM friendships f
 WHERE f.user_id > f.friend_id
   AND EXISTS (SELECT 1 FROM friendships r WHERE r.user_id = f.friend_id AND r.friend_id = f.user_id);
UPDATE friendships SET user_id = friend_id, friend_id = user_id WHERE user_id > friend_id;
");

            migrationBuilder.RenameColumn(
                name: "friend_id",
                table: "friendships",
                newName: "user2id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "friendships",
                newName: "user1id");

            migrationBuilder.RenameIndex(
                name: "ix_friendships_friend_id",
                table: "friendships",
                newName: "ix_friendships_user2id");

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "friend_requests",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Tidy existing requests so the new constraints hold: drop requests to oneself, mark pending requests
            // between people who are already friends as accepted, and where a pair has several pending requests
            // keep the oldest pending and mark the rest rejected.
            migrationBuilder.Sql(@"
UPDATE friend_requests SET updated_at = created_at;
DELETE FROM friend_requests WHERE from_user_id = to_user_id;
UPDATE friend_requests r SET status = 'accepted', updated_at = now()
 WHERE r.status = 'pending'
   AND EXISTS (SELECT 1 FROM friendships f
                WHERE f.user1id = LEAST(r.from_user_id, r.to_user_id)
                  AND f.user2id = GREATEST(r.from_user_id, r.to_user_id));
UPDATE friend_requests r SET status = 'rejected', updated_at = now()
  FROM (SELECT id, row_number() OVER (
               PARTITION BY LEAST(from_user_id, to_user_id), GREATEST(from_user_id, to_user_id)
               ORDER BY created_at, id) AS n
          FROM friend_requests WHERE status = 'pending') d
 WHERE r.id = d.id AND d.n > 1;
UPDATE friend_requests SET status = 'rejected' WHERE status NOT IN ('pending', 'accepted', 'rejected');
CREATE UNIQUE INDEX ix_friend_requests_one_pending_per_pair
    ON friend_requests (LEAST(from_user_id, to_user_id), GREATEST(from_user_id, to_user_id))
    WHERE status = 'pending';
");

            // Backfill: give a fresh unique code to every user whose code is missing or malformed (including
            // lower-case codes, since another user may already hold the upper-case form).
            migrationBuilder.Sql(@"
DO $$
DECLARE
    alphabet constant text := 'ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789';
    target uuid;
    candidate text;
BEGIN
    FOR target IN SELECT id FROM users WHERE friend_code IS NULL OR friend_code !~ '^[A-Z0-9]{5}$' LOOP
        LOOP
            candidate := '';
            FOR i IN 1..5 LOOP
                candidate := candidate || substr(alphabet, 1 + floor(random() * 36)::int, 1);
            END LOOP;
            EXIT WHEN NOT EXISTS (SELECT 1 FROM users WHERE friend_code = candidate);
        END LOOP;
        UPDATE users SET friend_code = candidate, updated_at = now() WHERE id = target;
    END LOOP;
END $$;
");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_friend_code_format",
                table: "users",
                sql: "friend_code ~ '^[A-Z0-9]{5}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_friendships_ordered_pair",
                table: "friendships",
                sql: "user1id < user2id");

            migrationBuilder.CreateIndex(
                name: "ix_friend_requests_from_user_id_status",
                table: "friend_requests",
                columns: new[] { "from_user_id", "status" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_friend_requests_not_self",
                table: "friend_requests",
                sql: "from_user_id <> to_user_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_friend_requests_status",
                table: "friend_requests",
                sql: "status IN ('pending', 'accepted', 'rejected')");

            migrationBuilder.AddForeignKey(
                name: "fk_friendships_users_user1id",
                table: "friendships",
                column: "user1id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_friendships_users_user2id",
                table: "friendships",
                column: "user2id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_friendships_users_user1id",
                table: "friendships");

            migrationBuilder.DropForeignKey(
                name: "fk_friendships_users_user2id",
                table: "friendships");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_friend_code_format",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_friendships_ordered_pair",
                table: "friendships");

            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_friend_requests_one_pending_per_pair;");

            migrationBuilder.DropIndex(
                name: "ix_friend_requests_from_user_id_status",
                table: "friend_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_friend_requests_not_self",
                table: "friend_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_friend_requests_status",
                table: "friend_requests");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "friend_requests");

            migrationBuilder.RenameColumn(
                name: "user2id",
                table: "friendships",
                newName: "friend_id");

            migrationBuilder.RenameColumn(
                name: "user1id",
                table: "friendships",
                newName: "user_id");

            migrationBuilder.RenameIndex(
                name: "ix_friendships_user2id",
                table: "friendships",
                newName: "ix_friendships_friend_id");

            // Back to one row per direction.
            migrationBuilder.Sql(@"
INSERT INTO friendships (user_id, friend_id, created_at)
SELECT friend_id, user_id, created_at FROM friendships
ON CONFLICT DO NOTHING;
");

            migrationBuilder.CreateIndex(
                name: "ix_friend_requests_from_user_id",
                table: "friend_requests",
                column: "from_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_friendships_users_friend_id",
                table: "friendships",
                column: "friend_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_friendships_users_user_id",
                table: "friendships",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
