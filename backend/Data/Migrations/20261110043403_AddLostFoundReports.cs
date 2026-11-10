using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLostFoundReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lost_found_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    item_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    bus_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bus_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    route = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    incident_date = table.Column<DateOnly>(type: "date", nullable: false),
                    incident_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    image_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    image_content_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    contact_information = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    admin_note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lost_found_reports", x => x.id);
                    table.CheckConstraint("ck_lost_found_reports_status", "status IN ('open', 'under-review', 'resolved', 'closed')");
                    table.CheckConstraint("ck_lost_found_reports_text", "item_name ~ '\\S' AND description ~ '\\S' AND contact_information ~ '\\S'");
                    table.CheckConstraint("ck_lost_found_reports_type", "report_type IN ('lost', 'found')");
                    table.ForeignKey(
                        name: "fk_lost_found_reports_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lost_found_reports_category",
                table: "lost_found_reports",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_lost_found_reports_report_type_status_created_at",
                table: "lost_found_reports",
                columns: new[] { "report_type", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_lost_found_reports_user_id_created_at",
                table: "lost_found_reports",
                columns: new[] { "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lost_found_reports");
        }
    }
}
