using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TransferOrchestration.TransferManagement.Infrastructure.Reconciliation;

namespace TransferOrchestration.TransferManagement.Application.Reconciliation;

internal interface IReconciliationDueWorkDispatcher
{
    Task<int> DispatchDueAsync(CancellationToken cancellationToken);
}

internal sealed class ReconciliationDueWorkDispatcher(
    IServiceScopeFactory scopeFactory,
    IReconciliationStore reconciliationStore,
    IOptions<ReconciliationOptions> options,
    TimeProvider timeProvider) : IReconciliationDueWorkDispatcher
{
    private readonly string _workerId = $"{Environment.MachineName}-reconciliation-{Guid.NewGuid():N}";

    public async Task<int> DispatchDueAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var claims = await reconciliationStore.ClaimDueAsync(
            _workerId,
            now,
            options.Value.LeaseDuration,
            options.Value.BatchSize,
            cancellationToken);

        var dispatched = 0;
        foreach (var claim in claims)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IReconciliationProcessStep>()
                    .ExecuteAsync(claim, cancellationToken);
                dispatched++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // One failed item must not prevent the rest of the bounded batch from continuing.
            }
        }

        return dispatched;
    }
}
