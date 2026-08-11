using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.TransferManagement.Application.Submission;

internal sealed record SubmitTransferCommand(
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    string? Currency,
    string? TransferType,
    string IdempotencyKey,
    Guid CorrelationId);

internal enum TransferSubmissionOutcome
{
    Accepted,
    Replay,
    Processing,
    Conflict,
    ValidationFailed,
    AuthorizationRejected,
    DailyLimitExceeded,
    FraudRejected
}

internal sealed record SubmitTransferResult(
    TransferSubmissionOutcome Outcome,
    Guid? TransferId = null,
    Guid? CorrelationId = null,
    TransferState? State = null,
    IReadOnlyList<string>? Errors = null);

internal interface ITransferSubmissionService
{
    Task<SubmitTransferResult> SubmitAsync(SubmitTransferCommand command, CancellationToken cancellationToken);
}
