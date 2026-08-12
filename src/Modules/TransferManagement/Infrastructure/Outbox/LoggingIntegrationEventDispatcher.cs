using Microsoft.Extensions.Logging;
using TransferOrchestration.TransferManagement.Contracts.IntegrationEvents;

namespace TransferOrchestration.TransferManagement.Infrastructure.Outbox;

internal sealed class LoggingIntegrationEventDispatcher(ILogger<LoggingIntegrationEventDispatcher> logger) : IIntegrationEventDispatcher
{
    private static readonly Action<ILogger, Guid, Guid, Guid?, string, Exception?> Dispatched =
        LoggerMessage.Define<Guid, Guid, Guid?, string>(LogLevel.Information, new EventId(1, nameof(Dispatched)),
            "Integration event dispatched in-process: {MessageId} {TransferId} correlation {CorrelationId} {Type}");

    public Task DispatchAsync(TransferCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dispatched(logger, integrationEvent.MessageId, integrationEvent.TransferId, integrationEvent.CorrelationId,
            TransferCompletedIntegrationEvent.EventType, null);
        return Task.CompletedTask;
    }
}
