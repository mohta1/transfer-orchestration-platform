namespace TransferOrchestration.TransferManagement.Infrastructure.Outbox;

internal sealed record OutboxClaim(
    long Id,
    Guid MessageId,
    Guid TransferId,
    string Type,
    string Payload,
    int Attempts,
    DateTimeOffset LockedUntilUtc);
