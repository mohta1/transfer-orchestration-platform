using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TransferOrchestration.TransferManagement.Infrastructure.Persistence.Migrations;

[DbContext(typeof(global::TransferOrchestration.TransferManagement.Infrastructure.Persistence.TransferManagementDbContext))]
[Migration("20260812120000_AddReconciliationRecords")]
public partial class AddReconciliationRecords : Migration
    {
        private static readonly string[] DueWorkColumns = ["next_attempt_at_utc", "id"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reconciliation_records",
                schema: "transfer_management",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    network_submission_reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_enquiry_result = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    last_error = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    locked_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    locked_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reconciliation_records", x => x.id);
                    table.CheckConstraint("ck_reconciliation_records_attempt_count", "attempt_count >= 0");
                    table.CheckConstraint(
                        "ck_reconciliation_records_status",
                        "(status = 1 AND next_attempt_at_utc IS NOT NULL) OR " +
                        "(status = 2 AND next_attempt_at_utc IS NULL) OR " +
                        "(status = 3 AND next_attempt_at_utc IS NULL)");
                    table.CheckConstraint("ck_reconciliation_records_timestamps", "updated_at_utc >= created_at_utc");
                    table.ForeignKey(
                        name: "FK_reconciliation_records_transfers_transfer_id",
                        column: x => x.transfer_id,
                        principalSchema: "transfer_management",
                        principalTable: "transfers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_records_due_work",
                schema: "transfer_management",
                table: "reconciliation_records",
                columns: DueWorkColumns,
                filter: "status = 1 AND next_attempt_at_utc IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_reconciliation_records_transfer_id",
                schema: "transfer_management",
                table: "reconciliation_records",
                column: "transfer_id",
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO transfer_management.reconciliation_records
                    (transfer_id, network_submission_reference, status, attempt_count,
                     next_attempt_at_utc, version, created_at_utc, updated_at_utc)
                SELECT transfer.id,
                       process.network_submission_reference,
                       1,
                       0,
                       COALESCE(process.next_attempt_at_utc, CURRENT_TIMESTAMP),
                       1,
                       CURRENT_TIMESTAMP,
                       CURRENT_TIMESTAMP
                FROM transfer_management.transfers AS transfer
                INNER JOIN transfer_management.transfer_process_states AS process
                    ON process.transfer_id = transfer.id
                WHERE transfer.state = 'SubmissionStatusUnknown'
                  AND process.network_submission_reference IS NOT NULL
                  AND process.next_action = 'EnquirePaymentStatus';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM transfer_management.reconciliation_records) THEN
                        RAISE EXCEPTION 'Cannot downgrade TASK-11 while reconciliation records exist.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "reconciliation_records",
                schema: "transfer_management");
        }
    }
