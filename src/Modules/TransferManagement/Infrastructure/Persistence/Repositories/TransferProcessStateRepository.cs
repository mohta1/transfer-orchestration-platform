using Microsoft.EntityFrameworkCore;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.TransferManagement.Infrastructure.Persistence.Repositories;

internal sealed class TransferProcessStateRepository(TransferManagementDbContext dbContext)
    : ITransferProcessStateRepository
{
    public Task<TransferProcessState?> GetAsync(TransferId transferId, CancellationToken cancellationToken) =>
        dbContext.TransferProcessStates.SingleOrDefaultAsync(state => state.TransferId == transferId, cancellationToken);

    public async Task AddAsync(TransferProcessState processState, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(processState);
        await dbContext.TransferProcessStates.AddAsync(processState, cancellationToken);
    }

    public async Task<IReadOnlyList<DueTransferProcess>> GetDueAsync(
        DateTimeOffset dueAtUtc,
        int maximumCount,
        CancellationToken cancellationToken) =>
        await dbContext.TransferProcessStates
            .AsNoTracking()
            .Where(state =>
                state.Status == TransferProcessStatus.Active &&
                state.NextAction != TransferProcessAction.None &&
                state.NextAttemptAtUtc != null &&
                state.NextAttemptAtUtc <= dueAtUtc)
            .OrderBy(state => state.NextAttemptAtUtc)
            .ThenBy(state => state.TransferId)
            .Take(maximumCount)
            .Select(state => new DueTransferProcess(
                state.TransferId,
                state.CorrelationId,
                state.Status,
                state.CurrentStep,
                state.NextAction,
                state.AttemptCount,
                state.NextAttemptAtUtc!.Value,
                state.CreatedAtUtc,
                state.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            var transferId = exception.Entries
                .Where(entry => entry.Entity is TransferProcessState)
                .Select(entry => ((TransferProcessState)entry.Entity).TransferId.Value)
                .FirstOrDefault();
            dbContext.ChangeTracker.Clear();
            throw new TransferProcessConcurrencyConflictException(transferId, exception);
        }
    }
}
