using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Infrastructure.FraudScreening;

namespace TransferOrchestration.TransferManagement.Application.FraudScreening;

internal interface IFraudScreeningDueWorkDispatcher
{
    Task<int> DispatchDueAsync(CancellationToken cancellationToken);
}

internal sealed class FraudScreeningDueWorkDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<FraudScreeningOptions> options,
    TimeProvider timeProvider) : IFraudScreeningDueWorkDispatcher
{
    public async Task<int> DispatchDueAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<DueTransferProcess> due;
        await using (var queryScope = scopeFactory.CreateAsyncScope())
        {
            due = await queryScope.ServiceProvider.GetRequiredService<ITransferProcessManager>()
                .GetDueForActionAsync(
                    TransferProcessAction.RequestFraudScreening,
                    timeProvider.GetUtcNow(),
                    100,
                    cancellationToken);
        }

        var dispatched = 0;
        foreach (var work in due)
        {
            await using var workScope = scopeFactory.CreateAsyncScope();
            var manager = workScope.ServiceProvider.GetRequiredService<ITransferProcessManager>();
            var now = timeProvider.GetUtcNow();
            var claim = await manager.TryClaimDueAsync(
                work.TransferId,
                TransferProcessAction.RequestFraudScreening,
                work.Version,
                now,
                now + options.Value.LeaseDuration,
                cancellationToken);
            if (claim is null)
            {
                continue;
            }

            await workScope.ServiceProvider.GetRequiredService<IFraudScreeningProcessStep>()
                .ExecuteAsync(work.TransferId, claim.ClaimedVersion, cancellationToken);
            dispatched++;
        }

        return dispatched;
    }
}
