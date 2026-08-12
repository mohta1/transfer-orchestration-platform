namespace TransferOrchestration.TransferManagement.Contracts.IntegrationEvents;

public interface IIntegrationEventDispatcher
{
    Task DispatchAsync(TransferCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken);
}
