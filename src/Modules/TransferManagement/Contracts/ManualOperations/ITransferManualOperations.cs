namespace TransferOrchestration.TransferManagement.Contracts.ManualOperations;

public interface ITransferManualOperations
{
    Task<ManualTransferOperationResult> RejectFromManualReviewAsync(
        ManualTransferOperationCommand command,
        CancellationToken cancellationToken);

    Task<ManualTransferOperationResult> ConfirmSettlementFromManualReviewAsync(
        ManualTransferOperationCommand command,
        CancellationToken cancellationToken);
}

public sealed record ManualTransferOperationCommand(
    Guid TransferId,
    string CommandId,
    string ActorId,
    string Reason,
    Guid CorrelationId,
    Guid? CausationId);

public enum ManualTransferOperationOutcome
{
    Succeeded,
    Replay,
    MissingReason,
    InvalidState,
    TransferNotFound,
    ReservationConflict,
    ContentionRetryExhausted
}

public sealed record ManualTransferOperationResult(
    ManualTransferOperationOutcome Outcome,
    Guid? TransferId = null,
    string? PreviousState = null,
    string? NewState = null,
    Guid? CorrelationId = null);
