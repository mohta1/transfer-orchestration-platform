using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.TransferManagement.Application.ProcessManagement;

internal interface ITransferProcessStateRepository
{
    Task<TransferProcessState?> GetAsync(TransferId transferId, CancellationToken cancellationToken);

    Task AddAsync(TransferProcessState processState, CancellationToken cancellationToken);

    Task<IReadOnlyList<DueTransferProcess>> GetDueAsync(
        DateTimeOffset dueAtUtc,
        int maximumCount,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
