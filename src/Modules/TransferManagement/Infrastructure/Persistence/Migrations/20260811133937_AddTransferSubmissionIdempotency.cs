using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransferOrchestration.TransferManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferSubmissionIdempotency : Migration
    {
        private static readonly string[] ScopeAndKeyColumns = ["scope", "idempotency_key"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "idempotency_records",
                schema: "transfer_management",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    fingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_records", x => x.id);
                    table.CheckConstraint("ck_idempotency_records_completion", "(status = 'Processing' AND transfer_id IS NULL AND completed_at_utc IS NULL) OR (status = 'Completed' AND transfer_id IS NOT NULL AND completed_at_utc IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "ux_idempotency_records_scope_key",
                schema: "transfer_management",
                table: "idempotency_records",
                columns: ScopeAndKeyColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_records",
                schema: "transfer_management");
        }
    }
}
