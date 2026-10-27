using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCommute.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletLedgerBalancesAndChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "balance_after",
                table: "wallet_transactions",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "gems_after",
                table: "wallet_transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_wallets_balance_not_negative",
                table: "wallets",
                sql: "balance >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_wallets_gems_not_negative",
                table: "wallets",
                sql: "gems >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_wallets_balance_not_negative",
                table: "wallets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_wallets_gems_not_negative",
                table: "wallets");

            migrationBuilder.DropColumn(
                name: "balance_after",
                table: "wallet_transactions");

            migrationBuilder.DropColumn(
                name: "gems_after",
                table: "wallet_transactions");
        }
    }
}
