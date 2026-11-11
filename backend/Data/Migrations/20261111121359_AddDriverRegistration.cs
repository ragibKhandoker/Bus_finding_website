using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDriverRegistration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "driver_bus_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    driver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bus_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_by_admin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ended_by_admin_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_driver_bus_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_driver_bus_assignments_buses_bus_id",
                        column: x => x.bus_id,
                        principalTable: "buses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_driver_bus_assignments_users_assigned_by_admin_id",
                        column: x => x.assigned_by_admin_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_driver_bus_assignments_users_driver_id",
                        column: x => x.driver_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "driver_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: false),
                    gender = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    license_number_encrypted = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    license_number_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    license_number_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    license_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    license_expiry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    driving_experience_years = table.Column<int>(type: "integer", nullable: false),
                    nid_number_encrypted = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    nid_number_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    nid_number_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    present_address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    district = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    area = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    emergency_contact = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    claimed_bus_id = table.Column<Guid>(type: "uuid", nullable: true),
                    claimed_bus_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    claimed_operator_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    claimed_route = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    bus_registration_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    employment_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    verification_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    account_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    resubmission_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    resubmission_fields = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reviewed_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_driver_profiles", x => x.id);
                    table.CheckConstraint("ck_driver_profiles_account_status", "account_status IN ('PENDING_VERIFICATION', 'ACTIVE', 'REJECTED')");
                    table.CheckConstraint("ck_driver_profiles_employment_type", "employment_type IN ('Full-time', 'Part-time', 'Contractual', 'Self-employed')");
                    table.CheckConstraint("ck_driver_profiles_experience", "driving_experience_years BETWEEN 0 AND 60");
                    table.CheckConstraint("ck_driver_profiles_license_type", "license_type IN ('Professional', 'Heavy Vehicle', 'Other')");
                    table.CheckConstraint("ck_driver_profiles_verification_status", "verification_status IN ('PENDING', 'UNDER_REVIEW', 'APPROVED', 'REJECTED', 'RESUBMISSION_REQUIRED')");
                    table.ForeignKey(
                        name: "fk_driver_profiles_buses_claimed_bus_id",
                        column: x => x.claimed_bus_id,
                        principalTable: "buses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_driver_profiles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "driver_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    driver_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    private_storage_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    content_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_driver_documents", x => x.id);
                    table.CheckConstraint("ck_driver_documents_type", "document_type IN ('license_front', 'license_back', 'nid_front', 'nid_back', 'profile_photo')");
                    table.ForeignKey(
                        name: "fk_driver_documents_driver_profiles_driver_profile_id",
                        column: x => x.driver_profile_id,
                        principalTable: "driver_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "driver_verification_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    driver_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    admin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    previous_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    new_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_driver_verification_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_driver_verification_history_driver_profiles_driver_profile_",
                        column: x => x.driver_profile_id,
                        principalTable: "driver_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_driver_verification_history_users_admin_id",
                        column: x => x.admin_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_driver_bus_assignments_assigned_by_admin_id",
                table: "driver_bus_assignments",
                column: "assigned_by_admin_id");

            migrationBuilder.CreateIndex(
                name: "ix_driver_bus_assignments_bus_id",
                table: "driver_bus_assignments",
                column: "bus_id");

            migrationBuilder.CreateIndex(
                name: "ix_driver_bus_assignments_driver_id_is_active",
                table: "driver_bus_assignments",
                columns: new[] { "driver_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_driver_bus_assignments_one_active",
                table: "driver_bus_assignments",
                columns: new[] { "driver_id", "bus_id" },
                unique: true,
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_driver_documents_driver_profile_id_document_type",
                table: "driver_documents",
                columns: new[] { "driver_profile_id", "document_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_driver_profiles_claimed_bus_id",
                table: "driver_profiles",
                column: "claimed_bus_id");

            migrationBuilder.CreateIndex(
                name: "ix_driver_profiles_license_number_hash",
                table: "driver_profiles",
                column: "license_number_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_driver_profiles_nid_number_hash",
                table: "driver_profiles",
                column: "nid_number_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_driver_profiles_submitted_at",
                table: "driver_profiles",
                column: "submitted_at");

            migrationBuilder.CreateIndex(
                name: "ix_driver_profiles_user_id",
                table: "driver_profiles",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_driver_profiles_verification_status_submitted_at",
                table: "driver_profiles",
                columns: new[] { "verification_status", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_driver_verification_history_admin_id",
                table: "driver_verification_history",
                column: "admin_id");

            migrationBuilder.CreateIndex(
                name: "ix_driver_verification_history_driver_profile_id_created_at",
                table: "driver_verification_history",
                columns: new[] { "driver_profile_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "driver_bus_assignments");

            migrationBuilder.DropTable(
                name: "driver_documents");

            migrationBuilder.DropTable(
                name: "driver_verification_history");

            migrationBuilder.DropTable(
                name: "driver_profiles");
        }
    }
}
