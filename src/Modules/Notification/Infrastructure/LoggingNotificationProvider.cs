using Microsoft.Extensions.Logging;
using TransferOrchestration.Notification.Contracts;
using TransferOrchestration.TransferManagement.Contracts.IntegrationEvents;

namespace TransferOrchestration.Notification.Infrastructure;

internal sealed class LoggingNotificationProvider(ILogger<LoggingNotificationProvider> logger) : INotificationProvider
{
    private static readonly Action<ILogger, Guid, Guid, Exception?> Notified =
        LoggerMessage.Define<Guid, Guid>(LogLevel.Information, new EventId(1, nameof(Notified)),
            "Transfer completion notification handled: {MessageId} {TransferId}");

    public Task NotifyTransferCompletedAsync(
        TransferCompletedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Notified(logger, integrationEvent.MessageId, integrationEvent.TransferId, null);
        return Task.CompletedTask;
    }
}
