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

        migrationBuilder.Sql(
            """
            UPDATE transfer_management.transfer_process_states AS process
            SET status = 'Active',
                current_step = 'ActionScheduled',
                next_action = 'SubmitToPaymentNetwork',
                next_attempt_at_utc = CURRENT_TIMESTAMP,
                updated_at_utc = GREATEST(process.updated_at_utc, CURRENT_TIMESTAMP),
                version = process.version + 1
            FROM transfer_management.transfers AS transfer
            WHERE transfer.id = process.transfer_id
              AND transfer.type = 'DomesticInterbank'
              AND transfer.state = 'BalanceReserved'
              AND process.status = 'Waiting'
              AND process.current_step = 'WaitingForOutcome'
              AND process.next_action = 'None'
              AND process.next_attempt_at_utc IS NULL
              AND process.network_submission_reference IS NULL;
            """);

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
