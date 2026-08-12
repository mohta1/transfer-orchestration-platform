using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.TransferManagement.Application.Reconciliation;

internal interface IReconciliationRecordRepository
{
    Task<ReconciliationRecord?> GetByTransferIdAsync(TransferId transferId, CancellationToken cancellationToken);

    Task AddAsync(ReconciliationRecord record, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

internal sealed record DueReconciliationRecord(
    long Id,
    TransferId TransferId,
    string NetworkSubmissionReference,
    int AttemptCount,
    long Version,
    DateTimeOffset NextAttemptAtUtc);

internal sealed record ReconciliationClaim(
    long Id,
    TransferId TransferId,
    string NetworkSubmissionReference,
    int AttemptCount,
    long Version,
    DateTimeOffset LockedUntilUtc);

internal interface IReconciliationStore
{
    Task<IReadOnlyList<ReconciliationClaim>> ClaimDueAsync(
        string workerId,
        DateTimeOffset nowUtc,
        TimeSpan leaseDuration,
        int batchSize,
        CancellationToken cancellationToken);

    Task<int> RenewClaimAsync(
        ReconciliationClaim claim,
        string workerId,
        DateTimeOffset leaseUntilUtc,
        CancellationToken cancellationToken);
}
