using TransferOrchestration.TransferManagement.Contracts.IntegrationEvents;

namespace TransferOrchestration.Notification.Contracts;

public interface INotificationProvider
{
    Task NotifyTransferCompletedAsync(
        TransferCompletedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken);
}
