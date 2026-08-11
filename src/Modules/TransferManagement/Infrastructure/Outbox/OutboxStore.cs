using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;

namespace TransferOrchestration.TransferManagement.Infrastructure.Outbox;

internal sealed class OutboxStore(TransferManagementDbContext dbContext)
{
    public async Task<IReadOnlyList<OutboxClaim>> ClaimAsync(
        string workerId, int batchSize, DateTimeOffset nowUtc, TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var claims = new List<OutboxClaim>();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = """
                WITH eligible AS (
                    SELECT "Id"
                    FROM transfer_management.outbox_messages
                    WHERE "Status" = 0
                      AND "NextAttemptAtUtc" <= @now
                      AND ("LockedUntilUtc" IS NULL OR "LockedUntilUtc" <= @now)
                    ORDER BY "NextAttemptAtUtc", "Id"
                    FOR UPDATE SKIP LOCKED
                    LIMIT @batchSize
                )
                UPDATE transfer_management.outbox_messages AS message
                SET "LockedBy" = @workerId, "LockedUntilUtc" = @lockedUntil
                FROM eligible
                WHERE message."Id" = eligible."Id"
                RETURNING message."Id", message."MessageId", message."TransferId", message."Type",
                          message."Payload"::text, message."Attempts", message."LockedUntilUtc";
                """;
            AddParameter(command, "now", NpgsqlDbType.TimestampTz, nowUtc);
            AddParameter(command, "batchSize", NpgsqlDbType.Integer, batchSize);
            AddParameter(command, "workerId", NpgsqlDbType.Varchar, workerId);
            AddParameter(command, "lockedUntil", NpgsqlDbType.TimestampTz, nowUtc + leaseDuration);

            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    claims.Add(new OutboxClaim(
                        reader.GetInt64(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
                        reader.GetString(4), reader.GetInt32(5), reader.GetFieldValue<DateTimeOffset>(6)));
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return claims;
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    public Task<int> MarkPublishedAsync(OutboxClaim claim, string workerId, DateTimeOffset nowUtc, CancellationToken token) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE transfer_management.outbox_messages
            SET "Status" = 1, "PublishedAtUtc" = {nowUtc}, "LockedBy" = NULL,
                "LockedUntilUtc" = NULL, "LastError" = NULL
            WHERE "Id" = {claim.Id} AND "Status" = 0 AND "LockedBy" = {workerId}
              AND "LockedUntilUtc" = {claim.LockedUntilUtc}
            """, token);

    public Task<int> MarkRetryableFailureAsync(
        OutboxClaim claim, string workerId, DateTimeOffset nextAttemptUtc, string error,
        bool deadLetter, CancellationToken token) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE transfer_management.outbox_messages
            SET "Status" = {(deadLetter ? 2 : 0)}, "Attempts" = "Attempts" + 1,
                "NextAttemptAtUtc" = {nextAttemptUtc}, "LastError" = {error},
                "LockedBy" = NULL, "LockedUntilUtc" = NULL
            WHERE "Id" = {claim.Id} AND "Status" = 0 AND "LockedBy" = {workerId}
              AND "LockedUntilUtc" = {claim.LockedUntilUtc}
            """, token);

    private static void AddParameter(IDbCommand command, string name, NpgsqlDbType type, object value)
    {
        var parameter = new NpgsqlParameter(name, type) { Value = value };
        command.Parameters.Add(parameter);
    }
}
