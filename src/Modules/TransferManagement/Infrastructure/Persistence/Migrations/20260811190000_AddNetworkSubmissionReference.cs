using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace TransferOrchestration.TransferManagement.Infrastructure.Persistence.Migrations;

[DbContext(typeof(TransferManagementDbContext))]
[Migration("20260811190000_AddNetworkSubmissionReference")]
public partial class AddNetworkSubmissionReference : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "network_submission_reference",
            schema: "transfer_management",
            table: "transfer_process_states",
            type: "character varying(80)",
            maxLength: 80,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ux_transfer_process_states_network_submission_reference",
            schema: "transfer_management",
            table: "transfer_process_states",
            column: "network_submission_reference",
            unique: true,
            filter: "network_submission_reference IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_transfer_process_states_network_submission_reference",
            schema: "transfer_management",
            table: "transfer_process_states");

        migrationBuilder.DropColumn(
            name: "network_submission_reference",
            schema: "transfer_management",
            table: "transfer_process_states");
    }
}
