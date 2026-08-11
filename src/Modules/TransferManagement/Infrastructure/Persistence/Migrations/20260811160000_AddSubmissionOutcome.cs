using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransferOrchestration.TransferManagement.Infrastructure.Persistence.Migrations;

[DbContext(typeof(TransferManagementDbContext))]
[Migration("20260811160000_AddSubmissionOutcome")]
public partial class AddSubmissionOutcome : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "result_outcome",
            schema: "transfer_management",
            table: "idempotency_records",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "result_outcome",
            schema: "transfer_management",
            table: "idempotency_records");
    }
}
