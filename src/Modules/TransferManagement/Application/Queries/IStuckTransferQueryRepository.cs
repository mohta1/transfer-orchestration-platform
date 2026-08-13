using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Application.Reconciliation;
using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.TransferManagement.Application.Queries;

internal sealed record StuckTransferCandidate(
    Guid TransferId,
    TransferState TransferState,
    TransferProcessStatus ProcessStatus,
    TransferProcessStep CurrentStep,
    TransferProcessAction NextAction,
    int AttemptCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset TransferUpdatedAtUtc,
    DateTimeOffset ProcessUpdatedAtUtc,
    DateTimeOffset? NextAttemptAtUtc,
    Guid CorrelationId,
    ReconciliationStatus? ReconciliationStatus,
    DateTimeOffset? ReconciliationNextAttemptAtUtc);

internal interface IStuckTransferQueryRepository
{
    Task<IReadOnlyList<StuckTransferCandidate>> ListCandidatesAsync(
        DateTimeOffset nowUtc,
        TimeSpan stateAgeThreshold,
        int maximumCount,
        CancellationToken cancellationToken);
}
