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
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM transfer_management.transfer_process_states
                    WHERE network_submission_reference IS NOT NULL
                       OR next_action IN ('EnquirePaymentStatus', 'ReleaseReservation')
                ) THEN
                    RAISE EXCEPTION 'Cannot downgrade TASK-08 while external payment submission state exists.';
                END IF;
            END $$;
            """);

        migrationBuilder.Sql(
            """
            UPDATE transfer_management.transfer_process_states AS process
            SET status = 'Waiting',
                current_step = 'WaitingForOutcome',
                next_action = 'None',
                next_attempt_at_utc = NULL,
                updated_at_utc = GREATEST(process.updated_at_utc, CURRENT_TIMESTAMP),
                version = process.version + 1
            FROM transfer_management.transfers AS transfer
            WHERE transfer.id = process.transfer_id
              AND transfer.type = 'DomesticInterbank'
              AND transfer.state = 'BalanceReserved'
              AND process.status = 'Active'
              AND process.current_step = 'ActionScheduled'
              AND process.next_action = 'SubmitToPaymentNetwork'
              AND process.network_submission_reference IS NULL;
            """);

        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM transfer_management.transfer_process_states
                    WHERE next_action IN ('SubmitToPaymentNetwork', 'EnquirePaymentStatus', 'ReleaseReservation')
                ) THEN
                    RAISE EXCEPTION 'Cannot downgrade TASK-08 while TASK-08-only process actions remain.';
                END IF;
            END $$;
            """);

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
