using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransferOrchestration.Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessedMessageClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "processed_at_utc",
                schema: "notification",
                table: "processed_messages",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "claimed_until_utc",
                schema: "notification",
                table: "processed_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "owner_id",
                schema: "notification",
                table: "processed_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_processed_messages_state",
                schema: "notification",
                table: "processed_messages",
                sql: "(processed_at_utc IS NOT NULL AND owner_id IS NULL AND claimed_until_utc IS NULL) OR (processed_at_utc IS NULL AND owner_id IS NOT NULL AND claimed_until_utc IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_processed_messages_state",
                schema: "notification",
                table: "processed_messages");

            migrationBuilder.DropColumn(
                name: "claimed_until_utc",
                schema: "notification",
                table: "processed_messages");

            migrationBuilder.DropColumn(
                name: "owner_id",
                schema: "notification",
                table: "processed_messages");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "processed_at_utc",
                schema: "notification",
                table: "processed_messages",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }
    }
}
