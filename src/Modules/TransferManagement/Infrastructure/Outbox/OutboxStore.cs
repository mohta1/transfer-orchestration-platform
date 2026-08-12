using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;

namespace TransferOrchestration.TransferManagement.Infrastructure.Outbox;

internal sealed class OutboxStore(TransferManagementDbContext dbContext)
{
    public async Task<OutboxClaim?> ClaimOneAsync(string workerId, TimeSpan leaseDuration, CancellationToken token)
    {
        await dbContext.Database.OpenConnectionAsync(token);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(token);
        try
        {
            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = """
                WITH db_clock AS (SELECT CURRENT_TIMESTAMP AS now_utc),
                eligible AS (
                    SELECT "Id"
                    FROM transfer_management.outbox_messages, db_clock
                    WHERE "Status" = 0
                      AND "NextAttemptAtUtc" <= db_clock.now_utc
                      AND ("LockedUntilUtc" IS NULL OR "LockedUntilUtc" <= db_clock.now_utc)
                    ORDER BY "NextAttemptAtUtc", "Id"
                    FOR UPDATE SKIP LOCKED LIMIT 1
                )
                UPDATE transfer_management.outbox_messages AS message
                SET "LockedBy" = @worker, "LockedUntilUtc" = db_clock.now_utc + @lease_duration
                FROM eligible, db_clock
                WHERE message."Id" = eligible."Id"
                RETURNING message."Id", message."MessageId", message."TransferId", message."CorrelationId",
                          message."Type", message."Payload"::text, message."Attempts", message."LockedUntilUtc";
                """;
            AddParameter(command, "worker", NpgsqlDbType.Varchar, workerId);
            AddParameter(command, "lease_duration", NpgsqlDbType.Interval, leaseDuration);
            await using var reader = await command.ExecuteReaderAsync(token);
            OutboxClaim? claim = null;
            if (await reader.ReadAsync(token))
                claim = new OutboxClaim(reader.GetInt64(0), reader.GetGuid(1), reader.GetGuid(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3), reader.GetString(4), reader.GetString(5),
                    reader.GetInt32(6), reader.GetFieldValue<DateTimeOffset>(7));
            await reader.DisposeAsync();
            await transaction.CommitAsync(token);
            return claim;
        }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }

    public async Task<OutboxClaim?> RenewBeforeDispatchAsync(OutboxClaim claim, string workerId, TimeSpan leaseDuration, CancellationToken token)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            UPDATE transfer_management.outbox_messages
            SET "LockedUntilUtc" = CURRENT_TIMESTAMP + @lease_duration
            WHERE "Id" = @id AND "Status" = 0 AND "LockedBy" = @worker
              AND "LockedUntilUtc" = @expected AND "LockedUntilUtc" > CURRENT_TIMESTAMP
            RETURNING "LockedUntilUtc";
            """;
        AddParameter(command, "lease_duration", NpgsqlDbType.Interval, leaseDuration);
        AddParameter(command, "id", NpgsqlDbType.Bigint, claim.Id);
        AddParameter(command, "worker", NpgsqlDbType.Varchar, workerId);
        AddParameter(command, "expected", NpgsqlDbType.TimestampTz, claim.LockedUntilUtc);
        await dbContext.Database.OpenConnectionAsync(token);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, token);
            if (!await reader.ReadAsync(token))
                return null;

            var lease = reader.GetFieldValue<DateTimeOffset>(0);
            return claim with { LockedUntilUtc = lease };
        }
        finally { await dbContext.Database.CloseConnectionAsync(); }
    }

    public Task<int> MarkPublishedAsync(OutboxClaim claim, string workerId, CancellationToken token) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE transfer_management.outbox_messages
            SET "Status" = 1, "PublishedAtUtc" = CURRENT_TIMESTAMP, "LockedBy" = NULL,
                "LockedUntilUtc" = NULL, "LastError" = NULL
            WHERE "Id" = {claim.Id} AND "Status" = 0 AND "LockedBy" = {workerId}
              AND "LockedUntilUtc" = {claim.LockedUntilUtc}
            """, token);

    public Task<int> MarkFailureAsync(OutboxClaim claim, string workerId, TimeSpan retryDelay, string error,
        bool deadLetter, CancellationToken token) => dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE transfer_management.outbox_messages
            SET "Status" = {(deadLetter ? 2 : 0)}, "Attempts" = "Attempts" + 1,
                "NextAttemptAtUtc" = CURRENT_TIMESTAMP + {retryDelay}, "LastError" = {error},
                "FirstFailureAtUtc" = COALESCE("FirstFailureAtUtc", CURRENT_TIMESTAMP),
                "LastFailureAtUtc" = CURRENT_TIMESTAMP, "LockedBy" = NULL, "LockedUntilUtc" = NULL
            WHERE "Id" = {claim.Id} AND "Status" = 0 AND "LockedBy" = {workerId}
              AND "LockedUntilUtc" = {claim.LockedUntilUtc}
            """, token);

    private static void AddParameter(IDbCommand command, string name, NpgsqlDbType type, object value) =>
        command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value });
}
