using Microsoft.EntityFrameworkCore;
using TransferOrchestration.TransferManagement.Application.Queries;
using TransferOrchestration.TransferManagement.Application.Reconciliation;
using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.TransferManagement.Infrastructure.Persistence.Repositories;

internal sealed class StuckTransferQueryRepository(TransferManagementDbContext dbContext)
    : IStuckTransferQueryRepository
{
    private static readonly TransferState[] EligibleStates =
        StuckTransferClassifier.EligibleWorkflowStates.ToArray();

    public async Task<IReadOnlyList<StuckTransferCandidate>> ListCandidatesAsync(
        DateTimeOffset nowUtc,
        TimeSpan stateAgeThreshold,
        int maximumCount,
        CancellationToken cancellationToken)
    {
        var thresholdBoundary = nowUtc - stateAgeThreshold;

        var rows = await (
                from transfer in dbContext.Transfers.AsNoTracking()
                join process in dbContext.TransferProcessStates.AsNoTracking()
                    on transfer.Id equals process.TransferId
                join reconciliation in dbContext.ReconciliationRecords.AsNoTracking()
                    on transfer.Id equals reconciliation.TransferId into reconciliationGroup
                from reconciliation in reconciliationGroup.DefaultIfEmpty()
                where EligibleStates.Contains(transfer.State)
                select new
                {
                    TransferId = transfer.Id.Value,
                    TransferState = transfer.State,
                    ProcessStatus = process.Status,
                    CurrentStep = process.CurrentStep,
                    NextAction = process.NextAction,
                    AttemptCount = process.AttemptCount,
                    CreatedAtUtc = transfer.CreatedAtUtc,
                    TransferUpdatedAtUtc = transfer.UpdatedAtUtc,
                    ProcessUpdatedAtUtc = process.UpdatedAtUtc,
                    NextAttemptAtUtc = process.NextAttemptAtUtc,
                    CorrelationId = process.CorrelationId,
                    ReconciliationStatus = reconciliation == null ? (ReconciliationStatus?)null : reconciliation.Status,
                    ReconciliationNextAttemptAtUtc = reconciliation == null ? null : reconciliation.NextAttemptAtUtc
                })
            .ToListAsync(cancellationToken);

        return rows
            .Where(row =>
            {
                var reference = StuckTransferClassifier.ReferenceTimestamp(
                    row.TransferUpdatedAtUtc,
                    row.ProcessUpdatedAtUtc);
                if (reference > thresholdBoundary)
                {
                    return false;
                }

                if (StuckTransferClassifier.IsFutureScheduledProcessWork(
                        row.ProcessStatus,
                        row.NextAttemptAtUtc,
                        nowUtc))
                {
                    return false;
                }

                if (StuckTransferClassifier.IsFutureScheduledReconciliationWork(
                        row.TransferState,
                        row.ReconciliationStatus,
                        row.ReconciliationNextAttemptAtUtc,
                        nowUtc))
                {
                    return false;
                }

                return StuckTransferClassifier.HasCrossedThreshold(reference, nowUtc, stateAgeThreshold);
            })
            .OrderBy(row => StuckTransferClassifier.ReferenceTimestamp(
                row.TransferUpdatedAtUtc,
                row.ProcessUpdatedAtUtc))
            .ThenBy(row => row.TransferId)
            .Take(maximumCount)
            .Select(row => new StuckTransferCandidate(
                row.TransferId,
                row.TransferState,
                row.ProcessStatus,
                row.CurrentStep,
                row.NextAction,
                row.AttemptCount,
                row.CreatedAtUtc,
                row.TransferUpdatedAtUtc,
                row.ProcessUpdatedAtUtc,
                row.NextAttemptAtUtc,
                row.CorrelationId,
                row.ReconciliationStatus,
                row.ReconciliationNextAttemptAtUtc))
            .ToList();
    }
}
