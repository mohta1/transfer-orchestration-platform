using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransferOrchestration.TransferManagement.Infrastructure.Persistence.Migrations;

[DbContext(typeof(TransferManagementDbContext))]
[Migration("20260811170000_AddCumulativeDailyTransferUsage")]
public partial class AddCumulativeDailyTransferUsage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("ck_idempotency_records_completion", "idempotency_records", "transfer_management");
        migrationBuilder.AddCheckConstraint("ck_idempotency_records_completion", "idempotency_records", "(status = 'Processing' AND completed_at_utc IS NULL) OR (status = 'Completed' AND transfer_id IS NOT NULL AND completed_at_utc IS NOT NULL)", "transfer_management");
        migrationBuilder.CreateTable(
            name: "daily_transfer_usages", schema: "transfer_management",
            columns: table => new
            {
                source_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                utc_day = table.Column<DateOnly>(type: "date", nullable: false),
                consumed_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_daily_transfer_usages", x => new { x.source_account_id, x.currency, x.utc_day });
                table.CheckConstraint("ck_daily_transfer_usage_positive", "consumed_amount > 0");
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("daily_transfer_usages", "transfer_management");
        migrationBuilder.DropCheckConstraint("ck_idempotency_records_completion", "idempotency_records", "transfer_management");
        migrationBuilder.AddCheckConstraint("ck_idempotency_records_completion", "idempotency_records", "(status = 'Processing' AND transfer_id IS NULL AND completed_at_utc IS NULL) OR (status = 'Completed' AND transfer_id IS NOT NULL AND completed_at_utc IS NOT NULL)", "transfer_management");
    }
}
