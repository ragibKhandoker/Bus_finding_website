using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRewardsAndPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_offer_transactions_reason",
                table: "offer_transactions");

            migrationBuilder.AddColumn<string>(
                name: "referral_code",
                table: "users",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cashback_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    referral_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    remaining = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    fare_basis = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    base_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    tier_bonus = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    campaign_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    offer_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    wallet_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cashback_transactions", x => x.id);
                    table.CheckConstraint("ck_cashback_transactions_remaining", "remaining >= 0 AND (amount <= 0 OR remaining <= amount) AND (amount > 0 OR remaining = 0)");
                    table.CheckConstraint("ck_cashback_transactions_source", "source IN ('trip', 'first-trip', 'referral', 'redeemed', 'expired')");
                    table.ForeignKey(
                        name: "fk_cashback_transactions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "membership_tiers",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    min_trips = table.Column<int>(type: "integer", nullable: false),
                    min_spend = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    extra_cashback_percent = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    extra_cap_per_trip = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    extra_monthly_cap = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    benefits = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_membership_tiers", x => x.code);
                    table.CheckConstraint("ck_membership_tiers_values", "min_trips >= 0 AND min_spend >= 0 AND extra_cashback_percent BETWEEN 0 AND 100 AND extra_cap_per_trip >= 0 AND extra_monthly_cap >= 0");
                });

            migrationBuilder.CreateTable(
                name: "referrals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    referrer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    referred_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    qualifying_trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    referrer_reward = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    referred_reward = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    qualified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rewarded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_referrals", x => x.id);
                    table.CheckConstraint("ck_referrals_not_self", "referrer_id <> referred_user_id");
                    table.CheckConstraint("ck_referrals_status", "status IN ('pending', 'qualified', 'rewarded', 'rejected')");
                    table.ForeignKey(
                        name: "fk_referrals_users_referred_user_id",
                        column: x => x.referred_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_referrals_users_referrer_id",
                        column: x => x.referrer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reward_audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reward_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reward_campaigns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    banner_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    theme = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    starts_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cashback_percent = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    cashback_fixed = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    max_cashback_per_trip = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    min_fare = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    min_tier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    new_users_only = table.Column<bool>(type: "boolean", nullable: false),
                    weekend_only = table.Column<bool>(type: "boolean", nullable: false),
                    first_trip_only = table.Column<bool>(type: "boolean", nullable: false),
                    budget = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    spent = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    max_redemptions = table.Column<int>(type: "integer", nullable: true),
                    redemptions = table.Column<int>(type: "integer", nullable: false),
                    announced_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reward_campaigns", x => x.id);
                    table.CheckConstraint("ck_reward_campaigns_budget", "spent >= 0 AND (budget IS NULL OR spent <= budget)");
                    table.CheckConstraint("ck_reward_campaigns_dates", "ends_at > starts_at");
                    table.CheckConstraint("ck_reward_campaigns_redemptions", "redemptions >= 0 AND (max_redemptions IS NULL OR redemptions <= max_redemptions)");
                    table.CheckConstraint("ck_reward_campaigns_status", "status IN ('active', 'paused', 'expired')");
                    table.CheckConstraint("ck_reward_campaigns_type", "type IN ('cashback', 'weekend', 'first-trip', 'festival', 'welcome', 'tier-exclusive')");
                    table.CheckConstraint("ck_reward_campaigns_values", "cashback_percent BETWEEN 0 AND 100 AND cashback_fixed >= 0 AND min_fare >= 0 AND (max_cashback_per_trip IS NULL OR max_cashback_per_trip >= 0)");
                });

            migrationBuilder.CreateTable(
                name: "reward_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    cashback_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    cashback_percent = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    cashback_fixed = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    cashback_min_fare = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    cashback_max_per_trip = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    cashback_max_per_user_monthly = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    cashback_expiry_days = table.Column<int>(type: "integer", nullable: false),
                    allow_coupon_stacking = table.Column<bool>(type: "boolean", nullable: false),
                    allow_coupon_with_cashback = table.Column<bool>(type: "boolean", nullable: false),
                    new_user_days = table.Column<int>(type: "integer", nullable: false),
                    first_trip_cashback = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    referral_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    referrer_reward = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    referred_reward = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    referral_apply_days = table.Column<int>(type: "integer", nullable: false),
                    max_referral_rewards_per_month = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reward_settings", x => x.id);
                    table.CheckConstraint("ck_reward_settings_single_row", "id = 1");
                    table.CheckConstraint("ck_reward_settings_values", "cashback_percent BETWEEN 0 AND 100 AND cashback_fixed >= 0 AND cashback_min_fare >= 0 AND cashback_max_per_trip >= 0 AND cashback_max_per_user_monthly >= 0 AND cashback_expiry_days >= 0 AND new_user_days >= 0 AND first_trip_cashback >= 0 AND referrer_reward >= 0 AND referred_reward >= 0 AND referral_apply_days >= 0 AND max_referral_rewards_per_month >= 0");
                });

            migrationBuilder.CreateTable(
                name: "trip_rewards",
                columns: table => new
                {
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fare = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    eligible = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cashback = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    tier_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_rewards", x => x.trip_id);
                    table.CheckConstraint("ck_trip_rewards_cashback", "cashback >= 0");
                    table.ForeignKey(
                        name: "fk_trip_rewards_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_memberships",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tier_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    eligible_trips = table.Column<int>(type: "integer", nullable: false),
                    eligible_spend = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    upgraded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_memberships", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_user_memberships_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "coupons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    discount_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    discount_value = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    min_fare = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    max_discount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    starts_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    total_limit = table.Column<int>(type: "integer", nullable: true),
                    per_user_limit = table.Column<int>(type: "integer", nullable: false),
                    redemption_count = table.Column<int>(type: "integer", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    audience = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    min_tier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    route_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    weekend_only = table.Column<bool>(type: "boolean", nullable: false),
                    valid_days_after_issue = table.Column<int>(type: "integer", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coupons", x => x.id);
                    table.CheckConstraint("ck_coupons_audience", "audience IN ('all', 'new-users', 'assigned')");
                    table.CheckConstraint("ck_coupons_code_format", "code ~ '^[A-Z0-9_-]{3,30}$'");
                    table.CheckConstraint("ck_coupons_dates", "expires_at IS NULL OR expires_at > starts_at");
                    table.CheckConstraint("ck_coupons_discount_type", "discount_type IN ('fixed', 'percent')");
                    table.CheckConstraint("ck_coupons_limits", "per_user_limit >= 1 AND redemption_count >= 0 AND (total_limit IS NULL OR (total_limit >= 1 AND redemption_count <= total_limit))");
                    table.CheckConstraint("ck_coupons_type", "type IN ('welcome', 'first-trip', 'weekend', 'seasonal', 'referral', 'fixed', 'percentage', 'campaign')");
                    table.CheckConstraint("ck_coupons_values", "discount_value > 0 AND (discount_type <> 'percent' OR discount_value <= 100) AND min_fare >= 0 AND (max_discount IS NULL OR max_discount > 0)");
                    table.ForeignKey(
                        name: "fk_coupons_reward_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "reward_campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "coupon_redemptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    coupon_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_coupon_id = table.Column<Guid>(type: "uuid", nullable: true),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fare = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    final_fare = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    mode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    wallet_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    gpay_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coupon_redemptions", x => x.id);
                    table.CheckConstraint("ck_coupon_redemptions_amounts", "discount > 0 AND final_fare >= 0 AND discount <= fare");
                    table.CheckConstraint("ck_coupon_redemptions_mode", "mode IN ('wallet', 'gpay')");
                    table.ForeignKey(
                        name: "fk_coupon_redemptions_coupons_coupon_id",
                        column: x => x.coupon_id,
                        principalTable: "coupons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_coupon_redemptions_trips_trip_id",
                        column: x => x.trip_id,
                        principalTable: "trips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_coupon_redemptions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_coupons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    coupon_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    issued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expiry_notified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_coupons", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_coupons_coupons_coupon_id",
                        column: x => x.coupon_id,
                        principalTable: "coupons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_coupons_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_users_referral_code",
                table: "users",
                column: "referral_code",
                unique: true,
                filter: "referral_code IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_offer_transactions_reason",
                table: "offer_transactions",
                sql: "reason IN ('welcome', 'opening', 'granted', 'adjusted', 'redeemed', 'trip', 'earned', 'first-trip', 'referral', 'expired')");

            migrationBuilder.CreateIndex(
                name: "ix_cashback_transactions_one_per_event",
                table: "cashback_transactions",
                column: "event_key",
                unique: true,
                filter: "event_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cashback_transactions_open_lots",
                table: "cashback_transactions",
                column: "expires_at",
                filter: "remaining > 0");

            migrationBuilder.CreateIndex(
                name: "ix_cashback_transactions_user_id_created_at",
                table: "cashback_transactions",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_coupon_redemptions_coupon_id_user_id",
                table: "coupon_redemptions",
                columns: new[] { "coupon_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_coupon_redemptions_one_per_reference",
                table: "coupon_redemptions",
                columns: new[] { "user_id", "reference" },
                unique: true,
                filter: "reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_coupon_redemptions_one_per_trip",
                table: "coupon_redemptions",
                columns: new[] { "trip_id", "coupon_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_coupon_redemptions_user_id_created_at",
                table: "coupon_redemptions",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_coupons_campaign_id",
                table: "coupons",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_coupons_code",
                table: "coupons",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_coupons_is_active_expires_at",
                table: "coupons",
                columns: new[] { "is_active", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_membership_tiers_rank",
                table: "membership_tiers",
                column: "rank",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_referrals_referred_user_id",
                table: "referrals",
                column: "referred_user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_referrals_referrer_id_status",
                table: "referrals",
                columns: new[] { "referrer_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_reward_audit_logs_action_created_at",
                table: "reward_audit_logs",
                columns: new[] { "action", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reward_audit_logs_created_at",
                table: "reward_audit_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_reward_campaigns_status_ends_at",
                table: "reward_campaigns",
                columns: new[] { "status", "ends_at" });

            migrationBuilder.CreateIndex(
                name: "ix_trip_rewards_user_id_eligible",
                table: "trip_rewards",
                columns: new[] { "user_id", "eligible" });

            migrationBuilder.CreateIndex(
                name: "ix_user_coupons_coupon_id",
                table: "user_coupons",
                column: "coupon_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_coupons_expires_at",
                table: "user_coupons",
                column: "expires_at",
                filter: "used_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_user_coupons_user_id_coupon_id",
                table: "user_coupons",
                columns: new[] { "user_id", "coupon_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_coupons_user_id_reference",
                table: "user_coupons",
                columns: new[] { "user_id", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_memberships_tier_code",
                table: "user_memberships",
                column: "tier_code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cashback_transactions");

            migrationBuilder.DropTable(
                name: "coupon_redemptions");

            migrationBuilder.DropTable(
                name: "membership_tiers");

            migrationBuilder.DropTable(
                name: "referrals");

            migrationBuilder.DropTable(
                name: "reward_audit_logs");

            migrationBuilder.DropTable(
                name: "reward_settings");

            migrationBuilder.DropTable(
                name: "trip_rewards");

            migrationBuilder.DropTable(
                name: "user_coupons");

            migrationBuilder.DropTable(
                name: "user_memberships");

            migrationBuilder.DropTable(
                name: "coupons");

            migrationBuilder.DropTable(
                name: "reward_campaigns");

            migrationBuilder.DropIndex(
                name: "ix_users_referral_code",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_offer_transactions_reason",
                table: "offer_transactions");

            migrationBuilder.DropColumn(
                name: "referral_code",
                table: "users");

            migrationBuilder.AddCheckConstraint(
                name: "ck_offer_transactions_reason",
                table: "offer_transactions",
                sql: "reason IN ('welcome', 'opening', 'granted', 'adjusted', 'redeemed', 'trip')");
        }
    }
}
