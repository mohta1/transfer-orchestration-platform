namespace TransferOrchestration.TransferManagement.Application.Persistence;

internal interface ITransferManagementTransaction
{
    Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
}
