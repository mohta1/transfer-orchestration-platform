namespace TransferOrchestration.TransferManagement.Application.Idempotency;

internal enum IdempotencyClaimOutcome
{
    Owner,
    Processing,
    Completed,
    Conflict
}

internal sealed record TransferSubmissionResult(Guid TransferId);

internal sealed record IdempotencyClaim(
    IdempotencyClaimOutcome Outcome,
    Guid? OwnerToken = null,
    TransferSubmissionResult? Result = null);
