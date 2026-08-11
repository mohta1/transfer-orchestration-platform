using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.TransferManagement.Application.ProcessManagement;

internal sealed record TransferProcessClaim(
    TransferId TransferId,
    long ClaimedVersion,
    int AttemptCount,
    DateTimeOffset LeaseUntilUtc);
