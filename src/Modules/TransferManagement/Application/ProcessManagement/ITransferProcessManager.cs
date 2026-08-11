using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.TransferManagement.Application.ProcessManagement;

internal interface ITransferProcessManager
{
    Task CreateWithTransferAsync(Transfer transfer, Guid correlationId, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task ScheduleAsync(TransferId transferId, TransferProcessAction nextAction, DateTimeOffset nextAttemptAtUtc, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task RecordAttemptAsync(TransferId transferId, DateTimeOffset nextAttemptAtUtc, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task MarkWaitingAsync(TransferId transferId, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task MarkWaitingAsync(TransferId transferId, long claimedVersion, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task RecordAttemptAsync(TransferId transferId, long claimedVersion, DateTimeOffset nextAttemptAtUtc, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task<TransferProcessClaim?> TryClaimDueAsync(TransferId transferId, TransferProcessAction action, long expectedVersion, DateTimeOffset nowUtc, DateTimeOffset leaseUntilUtc, CancellationToken cancellationToken);

    Task CompleteAsync(TransferId transferId, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task<IReadOnlyList<DueTransferProcess>> GetDueAsync(DateTimeOffset dueAtUtc, int maximumCount, CancellationToken cancellationToken);

    Task<IReadOnlyList<DueTransferProcess>> GetDueForActionAsync(
        TransferProcessAction action,
        DateTimeOffset dueAtUtc,
        int maximumCount,
        CancellationToken cancellationToken);
}
