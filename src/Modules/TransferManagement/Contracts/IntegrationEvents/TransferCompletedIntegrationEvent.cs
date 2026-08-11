namespace TransferOrchestration.TransferManagement.Contracts.IntegrationEvents;

public sealed record TransferCompletedIntegrationEvent(
    Guid MessageId,
    Guid TransferId,
    DateTimeOffset CompletedAtUtc)
{
    public const string EventType = "transfer.completed.v1";
}
