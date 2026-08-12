using TransferOrchestration.TransferManagement.Application.Persistence;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Contracts.Queries;
using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.TransferManagement.Application.Queries;

internal sealed class TransferQueries(
    ITransferRepository transferRepository,
    ITransferProcessStateRepository processStateRepository)
    : ITransferQueries
{
    public async Task<TransferDetailsDto?> GetByIdAsync(
        Guid transferId,
        CancellationToken cancellationToken)
    {
        if (transferId == Guid.Empty)
        {
            return null;
        }

        var id = new TransferId(transferId);
        var transfer = await transferRepository.GetByIdAsync(id, cancellationToken);
        if (transfer is null)
        {
            return null;
        }

        var process = await processStateRepository.GetAsync(id, cancellationToken);

        return new TransferDetailsDto(
            transfer.Id.Value,
            transfer.SourceAccountId,
            transfer.DestinationAccountId,
            transfer.Amount,
            transfer.Currency,
            transfer.Type.ToString(),
            transfer.State.ToString(),
            process?.CorrelationId,
            transfer.CreatedAtUtc,
            transfer.UpdatedAtUtc);
    }
}
