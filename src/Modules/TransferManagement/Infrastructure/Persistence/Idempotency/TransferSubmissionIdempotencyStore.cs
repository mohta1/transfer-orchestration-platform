using Microsoft.EntityFrameworkCore;
using TransferOrchestration.TransferManagement.Application.Idempotency;

namespace TransferOrchestration.TransferManagement.Infrastructure.Persistence.Idempotency;

internal sealed class TransferSubmissionIdempotencyStore(TransferManagementDbContext context)
    : ITransferSubmissionIdempotencyStore
{
    public async Task<IdempotencyClaim> TryClaimAsync(
        string idempotencyKey,
        string fingerprint,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        if (idempotencyKey.Length > 200)
        {
            throw new ArgumentException("Idempotency key cannot exceed 200 characters.", nameof(idempotencyKey));
        }

        if (fingerprint.Length != 64)
        {
            throw new ArgumentException("Fingerprint must be a SHA-256 hexadecimal value.", nameof(fingerprint));
        }

        var ownerToken = Guid.NewGuid();
        const string processingStatus = "Processing";
        var inserted = await context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO transfer_management.idempotency_records
                (id, scope, idempotency_key, fingerprint, status, created_at_utc)
            VALUES
                ({{ownerToken}}, {{IdempotencyRecord.TransferSubmissionScope}}, {{idempotencyKey}}, {{fingerprint}}, {{processingStatus}}, {{createdAtUtc}})
            ON CONFLICT (scope, idempotency_key) DO NOTHING
            """, cancellationToken);

        if (inserted == 1)
        {
            return new IdempotencyClaim(IdempotencyClaimOutcome.Owner, ownerToken);
        }

        var existing = await context.IdempotencyRecords
            .AsNoTracking()
            .SingleAsync(
                record => record.Scope == IdempotencyRecord.TransferSubmissionScope
                    && record.Key == idempotencyKey,
                cancellationToken);

        if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            return new IdempotencyClaim(IdempotencyClaimOutcome.Conflict);
        }

        return existing.Status == IdempotencyRecordStatus.Completed
            ? new IdempotencyClaim(
                IdempotencyClaimOutcome.Completed,
                Result: new TransferSubmissionResult(existing.TransferId!.Value))
            : new IdempotencyClaim(IdempotencyClaimOutcome.Processing);
    }

    public async Task CompleteAsync(
        Guid ownerToken,
        TransferSubmissionResult result,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        var affected = await context.IdempotencyRecords
            .Where(record => record.Id == ownerToken && record.Status == IdempotencyRecordStatus.Processing)
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(record => record.Status, IdempotencyRecordStatus.Completed)
                    .SetProperty(record => record.TransferId, result.TransferId)
                    .SetProperty(record => record.CompletedAtUtc, completedAtUtc),
                cancellationToken);

        if (affected != 1)
        {
            throw new InvalidOperationException("Only the current Processing claim owner can complete an idempotency record.");
        }
    }
}
