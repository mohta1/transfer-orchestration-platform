namespace TransferOrchestration.TransferManagement.Infrastructure.Outbox;

internal sealed record OutboxClaim(
    long Id,
    Guid MessageId,
    Guid TransferId,
    Guid? CorrelationId,
    string Type,
    string Payload,
    int Attempts,
    DateTimeOffset LockedUntilUtc);
