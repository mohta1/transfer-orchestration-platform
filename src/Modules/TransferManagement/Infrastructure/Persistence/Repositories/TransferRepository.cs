using Microsoft.EntityFrameworkCore;
using TransferOrchestration.TransferManagement.Application.Persistence;
using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.TransferManagement.Infrastructure.Persistence.Repositories;

internal sealed class TransferRepository(TransferManagementDbContext dbContext)
    : ITransferRepository
{
    public Task<Transfer?> GetByIdAsync(
        TransferId transferId,
        CancellationToken cancellationToken) =>
        dbContext.Transfers.SingleOrDefaultAsync(
            transfer => transfer.Id == transferId,
            cancellationToken);

    public async Task AddAsync(
        Transfer transfer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        await dbContext.Transfers.AddAsync(transfer, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await dbContext.SaveChangesAsync(cancellationToken);
}
