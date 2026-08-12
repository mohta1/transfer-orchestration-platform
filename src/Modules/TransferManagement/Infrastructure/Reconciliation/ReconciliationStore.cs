using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using TransferOrchestration.TransferManagement.Application.Reconciliation;
using TransferOrchestration.TransferManagement.Domain.Transfers;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;

namespace TransferOrchestration.TransferManagement.Infrastructure.Reconciliation;

internal sealed class ReconciliationStore(TransferManagementDbContext dbContext) : IReconciliationStore
{
    public async Task<IReadOnlyList<ReconciliationClaim>> ClaimDueAsync(
        string workerId,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        int batchSize,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = """
                WITH db_clock AS (SELECT @now_utc AS now_utc),
                eligible AS (
                    SELECT id
                    FROM transfer_management.reconciliation_records, db_clock
                    WHERE status = 1
                      AND next_attempt_at_utc <= db_clock.now_utc
                      AND (locked_until_utc IS NULL OR locked_until_utc <= db_clock.now_utc)
                    ORDER BY next_attempt_at_utc, id
                    FOR UPDATE SKIP LOCKED LIMIT @batch_size
                )
                UPDATE transfer_management.reconciliation_records AS record
                SET locked_by = @worker,
                    locked_until_utc = db_clock.now_utc + @lease_duration,
                    version = record.version + 1,
                    updated_at_utc = db_clock.now_utc
                FROM eligible, db_clock
                WHERE record.id = eligible.id
                RETURNING record.id, record.transfer_id, record.network_submission_reference,
                          record.attempt_count, record.version, record.locked_until_utc;
                """;
            AddParameter(command, "now_utc", NpgsqlDbType.TimestampTz, nowUtc);
            AddParameter(command, "batch_size", NpgsqlDbType.Integer, batchSize);
            AddParameter(command, "worker", NpgsqlDbType.Varchar, workerId);
            AddParameter(command, "lease_duration", NpgsqlDbType.Interval, leaseDuration);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var claims = new List<ReconciliationClaim>();
            while (await reader.ReadAsync(cancellationToken))
            {
                claims.Add(new ReconciliationClaim(
                    reader.GetInt64(0),
                    new TransferId(reader.GetGuid(1)),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetInt64(4),
                    reader.GetFieldValue<DateTimeOffset>(5)));
            }

            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
            return claims;
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    public async Task<int> RenewClaimAsync(
        ReconciliationClaim claim,
        string workerId,
        DateTimeOffset leaseUntilUtc,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                UPDATE transfer_management.reconciliation_records
                SET locked_until_utc = @lease_until
                WHERE id = @id
                  AND status = 1
                  AND locked_by = @worker
                  AND locked_until_utc = @expected
                  AND locked_until_utc > CURRENT_TIMESTAMP;
                """;
            AddParameter(command, "lease_until", NpgsqlDbType.TimestampTz, leaseUntilUtc);
            AddParameter(command, "id", NpgsqlDbType.Bigint, claim.Id);
            AddParameter(command, "worker", NpgsqlDbType.Varchar, workerId);
            AddParameter(command, "expected", NpgsqlDbType.TimestampTz, claim.LockedUntilUtc);
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static void AddParameter(IDbCommand command, string name, NpgsqlDbType type, object value) =>
        command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value });
}
