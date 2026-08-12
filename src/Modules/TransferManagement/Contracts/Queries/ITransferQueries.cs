namespace TransferOrchestration.TransferManagement.Contracts.Queries;

public interface ITransferQueries
{
    Task<TransferDetailsDto?> GetByIdAsync(
        Guid transferId,
        CancellationToken cancellationToken);
}

public sealed record TransferDetailsDto(
    Guid TransferId,
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    string Currency,
    string TransferType,
    string State,
    Guid? CorrelationId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
