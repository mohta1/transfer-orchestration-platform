using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.TransferManagement.Application.Persistence;

internal interface ITransferRepository
{
    Task<Transfer?> GetByIdAsync(
        TransferId transferId,
        CancellationToken cancellationToken);

    Task AddAsync(
        Transfer transfer,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
