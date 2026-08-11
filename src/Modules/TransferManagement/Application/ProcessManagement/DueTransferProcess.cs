using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.TransferManagement.Application.ProcessManagement;

internal sealed record DueTransferProcess(
    TransferId TransferId,
    Guid CorrelationId,
    TransferProcessStatus Status,
    TransferProcessStep CurrentStep,
    TransferProcessAction NextAction,
    int AttemptCount,
    long Version,
    DateTimeOffset NextAttemptAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
