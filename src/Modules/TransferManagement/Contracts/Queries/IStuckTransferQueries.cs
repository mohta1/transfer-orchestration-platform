namespace TransferOrchestration.TransferManagement.Contracts.Queries;

public interface IStuckTransferQueries
{
    Task<StuckTransferQueryResult> ListAsync(
        StuckTransferQueryRequest request,
        CancellationToken cancellationToken);
}

public sealed record StuckTransferQueryRequest(int? MaxResults);

public sealed record StuckTransferQueryResult(
    IReadOnlyList<StuckTransferItemDto> Items,
    int StateAgeThresholdSeconds,
    DateTimeOffset QueriedAtUtc);

public sealed record StuckTransferItemDto(
    Guid TransferId,
    string TransferState,
    string ProcessStatus,
    string CurrentStep,
    string NextAction,
    int AttemptCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? NextAttemptAtUtc,
    long AgeSeconds,
    DateTimeOffset ThresholdCrossedAtUtc,
    Guid CorrelationId,
    string Category);

public sealed class StuckTransferQueryValidationException(string message) : Exception(message);
