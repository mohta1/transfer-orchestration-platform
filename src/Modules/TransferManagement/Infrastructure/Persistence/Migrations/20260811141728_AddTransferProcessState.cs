using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransferOrchestration.TransferManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferProcessState : Migration
    {
        private static readonly string[] DueWorkColumns = ["next_attempt_at_utc", "transfer_id"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "transfer_process_states",
                schema: "transfer_management",
                columns: table => new
                {
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    current_step = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    next_action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfer_process_states", x => x.transfer_id);
                    table.CheckConstraint("ck_transfer_process_states_actionability", "(status = 'Active' AND next_action <> 'None' AND next_attempt_at_utc IS NOT NULL AND current_step IN ('Created', 'ActionScheduled')) OR (status = 'Waiting' AND next_action = 'None' AND next_attempt_at_utc IS NULL AND current_step = 'WaitingForOutcome') OR (status = 'Completed' AND next_action = 'None' AND next_attempt_at_utc IS NULL AND current_step = 'Completed')");
                    table.CheckConstraint("ck_transfer_process_states_attempt_count", "attempt_count >= 0");
                    table.CheckConstraint("ck_transfer_process_states_correlation_id", "correlation_id <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("ck_transfer_process_states_timestamps", "updated_at_utc >= created_at_utc");
                    table.ForeignKey(
                        name: "FK_transfer_process_states_transfers_transfer_id",
                        column: x => x.transfer_id,
                        principalSchema: "transfer_management",
                        principalTable: "transfers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_transfer_process_states_due_work",
                schema: "transfer_management",
                table: "transfer_process_states",
                columns: DueWorkColumns,
                filter: "status = 'Active' AND next_action <> 'None' AND next_attempt_at_utc IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transfer_process_states",
                schema: "transfer_management");
        }
    }
}
