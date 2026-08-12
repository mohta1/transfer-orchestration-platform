using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TransferOrchestration.AuditOperations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationsAuditRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit_operations");

            migrationBuilder.CreateTable(
                name: "operations_audit_records",
                schema: "audit_operations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    command_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    actor_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    new_state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    causation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operations_audit_records", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_operations_audit_records_command_id",
                schema: "audit_operations",
                table: "operations_audit_records",
                column: "command_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operations_audit_records_correlation_id",
                schema: "audit_operations",
                table: "operations_audit_records",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "IX_operations_audit_records_transfer_id",
                schema: "audit_operations",
                table: "operations_audit_records",
                column: "transfer_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operations_audit_records",
                schema: "audit_operations");
        }
    }
}
