using Microsoft.Extensions.Logging;
using TransferOrchestration.Notification.Contracts;
using TransferOrchestration.TransferManagement.Contracts.IntegrationEvents;
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace TransferOrchestration.Notification.Infrastructure;

internal sealed class LoggingNotificationProvider(
    ILogger<LoggingNotificationProvider> logger,
    IOptions<LoggingNotificationProviderOptions> options,
    TimeProvider timeProvider) : INotificationProvider
{
    private readonly ConcurrentDictionary<NotificationIdempotencyKey, DateTimeOffset> _delivered = new();
    private readonly object _gate = new();
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
        lock (_gate)
        {
            var now = timeProvider.GetUtcNow();
            foreach (var expired in _delivered.Where(item => item.Value <= now).Select(item => item.Key))
                _delivered.TryRemove(expired, out _);
            if (_delivered.ContainsKey(idempotencyKey)) return Task.CompletedTask;
            while (_delivered.Count >= options.Value.Capacity)
            {
                var oldest = _delivered.MinBy(item => item.Value).Key;
                _delivered.TryRemove(oldest, out _);
            }
            try
            {
                Notified(logger, integrationEvent.MessageId, integrationEvent.TransferId, null);
                _delivered[idempotencyKey] = now + options.Value.Retention;
            }
            catch
            {
                _delivered.TryRemove(idempotencyKey, out _);
                throw;
            }
        }
        return Task.CompletedTask;
    }

    internal int RetainedCount => _delivered.Count;
}
