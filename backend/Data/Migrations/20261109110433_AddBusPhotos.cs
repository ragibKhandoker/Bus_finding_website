using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBusPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bus_photos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bus_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    content_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    credit = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    uploaded_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bus_photos", x => x.id);
                    table.ForeignKey(
                        name: "fk_bus_photos_buses_bus_id",
                        column: x => x.bus_id,
                        principalTable: "buses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_bus_photos_users_uploaded_by_id",
                        column: x => x.uploaded_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bus_photos_bus_id",
                table: "bus_photos",
                column: "bus_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bus_photos_storage_key",
                table: "bus_photos",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bus_photos_uploaded_by_id",
                table: "bus_photos",
                column: "uploaded_by_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bus_photos");
        }
    }
}
