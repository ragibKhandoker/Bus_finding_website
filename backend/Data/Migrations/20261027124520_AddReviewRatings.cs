using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    // Bus reviews & ratings (F13): reviews get a 1-5 star rating, and a bus's average comes from its reviews.
    // Every row of the older ratings table (one per user per bus) is copied onto a review with the same bus, user,
    // comment and times (fractional values are rounded to whole stars), so no rating is lost. The ratings table is
    // left in place but is no longer used. Cached like/dislike counts are recounted from review_reactions.
    /// <inheritdoc />
    public partial class AddReviewRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_review_replies_review_id",
                table: "review_replies");

            migrationBuilder.AddColumn<int>(
                name: "rating",
                table: "reviews",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(@"
INSERT INTO reviews (id, bus_id, user_id, user_name, rating, comment, likes, dislikes, created_at, updated_at)
SELECT gen_random_uuid(), r.bus_id, r.user_id, u.name, LEAST(GREATEST(round(r.value)::int, 1), 5), r.comment, 0, 0, r.created_at, r.updated_at
  FROM ratings r JOIN users u ON u.id = r.user_id;
UPDATE reviews rv SET
    likes = (SELECT count(*) FROM review_reactions x WHERE x.review_id = rv.id AND x.is_like),
    dislikes = (SELECT count(*) FROM review_reactions x WHERE x.review_id = rv.id AND NOT x.is_like);
");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_rated",
                table: "reviews",
                columns: new[] { "bus_id", "rating" },
                filter: "rating IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reviews_rating_range",
                table: "reviews",
                sql: "rating IS NULL OR rating BETWEEN 1 AND 5");

            migrationBuilder.CreateIndex(
                name: "ix_review_replies_review_id_created_at",
                table: "review_replies",
                columns: new[] { "review_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reviews_rated",
                table: "reviews");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reviews_rating_range",
                table: "reviews");

            migrationBuilder.DropIndex(
                name: "ix_review_replies_review_id_created_at",
                table: "review_replies");

            // The reviews copied from the ratings table (still intact) are removed again.
            migrationBuilder.Sql(@"
DELETE FROM reviews rv USING ratings r
 WHERE rv.bus_id = r.bus_id AND rv.user_id = r.user_id AND rv.created_at = r.created_at;
");

            migrationBuilder.DropColumn(
                name: "rating",
                table: "reviews");

            migrationBuilder.CreateIndex(
                name: "ix_review_replies_review_id",
                table: "review_replies",
                column: "review_id");
        }
    }
}
