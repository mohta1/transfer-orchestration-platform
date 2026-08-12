using TransferOrchestration.TransferManagement.Contracts.IntegrationEvents;

namespace TransferOrchestration.Notification.Contracts;

public interface INotificationProvider
{
    Task NotifyTransferCompletedAsync(
        NotificationIdempotencyKey idempotencyKey,
        TransferCompletedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken);
}

public readonly record struct NotificationIdempotencyKey(string ConsumerName, Guid MessageId)
{
    public override string ToString() => $"{ConsumerName}:{MessageId:D}";
}
