using Microsoft.Extensions.Logging;
using TransferOrchestration.TransferManagement.Contracts.IntegrationEvents;

namespace TransferOrchestration.TransferManagement.Infrastructure.Outbox;

internal sealed class LoggingIntegrationEventDispatcher(ILogger<LoggingIntegrationEventDispatcher> logger) : IIntegrationEventDispatcher
{
    private static readonly Action<ILogger, Guid, Guid, string, Exception?> Dispatched =
        LoggerMessage.Define<Guid, Guid, string>(LogLevel.Information, new EventId(1, nameof(Dispatched)),
            "Integration event dispatched in-process: {MessageId} {TransferId} {Type}");

    public Task DispatchAsync(TransferCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dispatched(logger, integrationEvent.MessageId, integrationEvent.TransferId,
            TransferCompletedIntegrationEvent.EventType, null);
        return Task.CompletedTask;
    }
}
