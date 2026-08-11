using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransferOrchestration.AccountBalance.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAccountBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "account_balance");

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "account_balance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    available_balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    reserved_balance = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.id);
                    table.CheckConstraint("ck_accounts_available_balance_non_negative", "available_balance >= 0");
                    table.CheckConstraint("ck_accounts_reserved_balance_non_negative", "reserved_balance >= 0");
                });

            migrationBuilder.CreateTable(
                name: "balance_reservations",
                schema: "account_balance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finalised_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_balance_reservations", x => x.id);
                    table.CheckConstraint("ck_balance_reservations_amount_positive", "amount > 0");
                    table.ForeignKey(
                        name: "FK_balance_reservations_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "account_balance",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_balance_reservations_account_id",
                schema: "account_balance",
                table: "balance_reservations",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ux_balance_reservations_transfer_id",
                schema: "account_balance",
                table: "balance_reservations",
                column: "transfer_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "balance_reservations",
                schema: "account_balance");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "account_balance");
        }
    }
}
