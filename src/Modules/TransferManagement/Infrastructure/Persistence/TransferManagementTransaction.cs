using TransferOrchestration.TransferManagement.Application.Persistence;

namespace TransferOrchestration.TransferManagement.Infrastructure.Persistence;

internal sealed class TransferManagementTransaction(TransferManagementDbContext context)
    : ITransferManagementTransaction
{
    public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
