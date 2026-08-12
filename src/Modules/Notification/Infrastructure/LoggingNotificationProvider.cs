using Microsoft.Extensions.Logging;
using TransferOrchestration.Notification.Contracts;
using TransferOrchestration.TransferManagement.Contracts.IntegrationEvents;
using System.Collections.Concurrent;

namespace TransferOrchestration.Notification.Infrastructure;

internal sealed class LoggingNotificationProvider(ILogger<LoggingNotificationProvider> logger) : INotificationProvider
{
    private readonly ConcurrentDictionary<NotificationIdempotencyKey, byte> _delivered = new();
    private static readonly Action<ILogger, Guid, Guid, Exception?> Notified =
        LoggerMessage.Define<Guid, Guid>(LogLevel.Information, new EventId(1, nameof(Notified)),
            "Transfer completion notification handled: {MessageId} {TransferId}");

    public Task NotifyTransferCompletedAsync(
        NotificationIdempotencyKey idempotencyKey,
        TransferCompletedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (idempotencyKey.MessageId != integrationEvent.MessageId)
            throw new ArgumentException("The provider idempotency key must contain the integration-event message identifier.",
                nameof(idempotencyKey));
        if (_delivered.TryAdd(idempotencyKey, 0))
            Notified(logger, integrationEvent.MessageId, integrationEvent.TransferId, null);
        return Task.CompletedTask;
    }
}
