using TransferOrchestration.BuildingBlocks.Domain;
using TransferOrchestration.TransferManagement.Application.Persistence;
using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.TransferManagement.Application.ProcessManagement;

internal sealed class TransferProcessManager(
    ITransferRepository transferRepository,
    ITransferProcessStateRepository processRepository) : ITransferProcessManager
{
    public async Task CreateWithTransferAsync(Transfer transfer, Guid correlationId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        var processState = TransferProcessState.Create(transfer.Id, correlationId, nowUtc);
        await transferRepository.AddAsync(transfer, cancellationToken);
        await processRepository.AddAsync(processState, cancellationToken);
        await processRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task ScheduleAsync(TransferId transferId, TransferProcessAction nextAction, DateTimeOffset nextAttemptAtUtc, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var state = await GetRequiredAsync(transferId, cancellationToken);
        state.Schedule(nextAction, nextAttemptAtUtc, nowUtc);
        await processRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordAttemptAsync(TransferId transferId, DateTimeOffset nextAttemptAtUtc, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var state = await GetRequiredAsync(transferId, cancellationToken);
        state.RecordAttempt(nextAttemptAtUtc, nowUtc);
        await processRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkWaitingAsync(TransferId transferId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var state = await GetRequiredAsync(transferId, cancellationToken);
        state.MarkWaiting(nowUtc);
        await processRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync(TransferId transferId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var state = await GetRequiredAsync(transferId, cancellationToken);
        state.Complete(nowUtc);
        await processRepository.SaveChangesAsync(cancellationToken);
    }

    public Task<IReadOnlyList<DueTransferProcess>> GetDueAsync(DateTimeOffset dueAtUtc, int maximumCount, CancellationToken cancellationToken)
    {
        if (dueAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainException("Due-work query time must be UTC.");
        }

        if (maximumCount <= 0 || maximumCount > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount), "Result count must be between 1 and 1000.");
        }

        return processRepository.GetDueAsync(dueAtUtc, maximumCount, cancellationToken);
    }

    private async Task<TransferProcessState> GetRequiredAsync(TransferId transferId, CancellationToken cancellationToken) =>
        await processRepository.GetAsync(transferId, cancellationToken)
        ?? throw new DomainException($"Transfer process '{transferId.Value}' was not found.");
}
