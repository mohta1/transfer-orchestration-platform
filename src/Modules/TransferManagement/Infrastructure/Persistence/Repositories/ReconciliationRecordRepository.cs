using Microsoft.EntityFrameworkCore;
using TransferOrchestration.TransferManagement.Application.Reconciliation;
using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.TransferManagement.Infrastructure.Persistence.Repositories;

internal sealed class ReconciliationRecordRepository(TransferManagementDbContext dbContext)
    : IReconciliationRecordRepository
{
    public Task<ReconciliationRecord?> GetByTransferIdAsync(
        TransferId transferId,
        CancellationToken cancellationToken) =>
        dbContext.ReconciliationRecords.SingleOrDefaultAsync(
            record => record.TransferId == transferId,
            cancellationToken);

    public async Task AddAsync(ReconciliationRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        await dbContext.ReconciliationRecords.AddAsync(record, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            dbContext.ChangeTracker.Clear();
            throw new ReconciliationConcurrencyConflictException(exception);
        }
    }
}

internal sealed class ReconciliationConcurrencyConflictException(DbUpdateConcurrencyException innerException)
    : Exception("Reconciliation record concurrency conflict.", innerException);
