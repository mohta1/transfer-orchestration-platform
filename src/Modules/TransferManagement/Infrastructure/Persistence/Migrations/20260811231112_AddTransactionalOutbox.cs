using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TransferOrchestration.TransferManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionalOutbox : Migration
    {
        private static readonly string[] EligibilityColumns =
            ["Status", "NextAttemptAtUtc", "LockedUntilUtc", "Id"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "transfer_management",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransferId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LockedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LockedUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                    table.CheckConstraint("ck_outbox_attempts", "\"Attempts\" >= 0");
                    table.CheckConstraint("ck_outbox_dead_letter", "\"Status\" <> 2 OR (\"LockedBy\" IS NULL AND \"LockedUntilUtc\" IS NULL)");
                    table.CheckConstraint("ck_outbox_lock", "(\"LockedBy\" IS NULL) = (\"LockedUntilUtc\" IS NULL)");
                    table.CheckConstraint("ck_outbox_published", "\"Status\" <> 1 OR \"PublishedAtUtc\" IS NOT NULL");
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_eligibility",
                schema: "transfer_management",
                table: "outbox_messages",
                columns: EligibilityColumns);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_MessageId",
                schema: "transfer_management",
                table: "outbox_messages",
                column: "MessageId",
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO transfer_management.outbox_messages
                    ("MessageId", "TransferId", "Type", "Payload", "OccurredAtUtc", "Status",
                     "Attempts", "NextAttemptAtUtc")
                SELECT message_id,
                       transfer.id,
                       'transfer.completed.v1',
                       jsonb_build_object(
                           'MessageId', message_id,
                           'TransferId', transfer.id,
                           'CompletedAtUtc', transfer.updated_at_utc),
                       transfer.updated_at_utc,
                       0,
                       0,
                       transfer.updated_at_utc
                FROM transfer_management.transfers AS transfer
                CROSS JOIN LATERAL (
                    SELECT gen_random_uuid() AS message_id
                    WHERE transfer.id IS NOT NULL
                ) AS generated
                WHERE transfer.state = 'Completed';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM transfer_management.outbox_messages) THEN
                        RAISE EXCEPTION 'Cannot downgrade TASK-09 while durable Outbox messages exist.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "transfer_management");
        }
    }
}
