using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.TransferManagement.Application.Reconciliation;

internal interface IReconciliationScheduling
{
    Task EnsureScheduledAsync(
        TransferId transferId,
        string networkSubmissionReference,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}

internal sealed class ReconciliationScheduling(IReconciliationRecordRepository repository) : IReconciliationScheduling
{
    public async Task EnsureScheduledAsync(
        TransferId transferId,
        string networkSubmissionReference,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var existing = await repository.GetByTransferIdAsync(transferId, cancellationToken);
        if (existing is not null)
        {
            return;
        }

        await repository.AddAsync(
            ReconciliationRecord.ScheduleForUnknown(transferId, networkSubmissionReference, nowUtc),
            cancellationToken);
    }
}
