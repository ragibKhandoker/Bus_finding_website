using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    // One-to-one chat (F10):
    //  - chat_messages.read_at records when the recipient read a message (existing read messages get created_at);
    //  - chats are stored with the smaller user id first, the same order as friendships, enforced by a check;
    //  - blank messages are refused by the database; an index keeps unread counts fast.
    // Existing chats were ordered by Guid.CompareTo, which differs from PostgreSQL's uuid order. Rows in the wrong
    // order are swapped; if a pair somehow has a row each way, the messages move to the correctly ordered row and the
    // other row is removed. Messages that are only whitespace (which the old code could save) are deleted.
    /// <inheritdoc />
    public partial class AddChatReadStateAndPairOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "read_at",
                table: "chat_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(@"
UPDATE chat_messages SET read_at = created_at WHERE read AND read_at IS NULL;

UPDATE chat_messages m SET chat_id = good.id
  FROM chats bad JOIN chats good ON good.user1id = bad.user2id AND good.user2id = bad.user1id
 WHERE m.chat_id = bad.id AND bad.user1id > bad.user2id;
UPDATE chats good SET last_message_at = GREATEST(good.last_message_at, bad.last_message_at)
  FROM chats bad
 WHERE good.user1id = bad.user2id AND good.user2id = bad.user1id AND bad.user1id > bad.user2id;
DELETE FROM chats bad USING chats good
 WHERE good.user1id = bad.user2id AND good.user2id = bad.user1id AND bad.user1id > bad.user2id;
DELETE FROM chats WHERE user1id = user2id;
UPDATE chats SET user1id = user2id, user2id = user1id WHERE user1id > user2id;

DELETE FROM chat_messages WHERE text !~ '\S';
");

            migrationBuilder.AddCheckConstraint(
                name: "ck_chats_ordered_pair",
                table: "chats",
                sql: "user1id < user2id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_unread",
                table: "chat_messages",
                columns: new[] { "chat_id", "sender_id" },
                filter: "read = false");

            migrationBuilder.AddCheckConstraint(
                name: "ck_chat_messages_text_not_blank",
                table: "chat_messages",
                sql: "text ~ '\\S'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_chats_ordered_pair",
                table: "chats");

            migrationBuilder.DropIndex(
                name: "ix_chat_messages_unread",
                table: "chat_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_chat_messages_text_not_blank",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "read_at",
                table: "chat_messages");
        }
    }
}
