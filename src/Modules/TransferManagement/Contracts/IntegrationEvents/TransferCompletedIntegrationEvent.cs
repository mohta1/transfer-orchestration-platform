namespace TransferOrchestration.TransferManagement.Contracts.IntegrationEvents;

public sealed record TransferCompletedIntegrationEvent(
    Guid MessageId,
    Guid TransferId,
    DateTimeOffset CompletedAtUtc,
    Guid? CorrelationId)
{
    public const string EventType = "transfer.completed.v1";
}
