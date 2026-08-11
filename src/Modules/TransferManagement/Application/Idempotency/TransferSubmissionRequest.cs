namespace TransferOrchestration.TransferManagement.Application.Idempotency;

internal sealed record TransferSubmissionRequest(
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    string Currency,
    Domain.Transfers.TransferType Type);
