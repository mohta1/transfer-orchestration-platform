using Microsoft.Extensions.DependencyInjection;
using TransferOrchestration.TransferManagement.Application.BalanceReservation;

namespace TransferOrchestration.TransferManagement.Application.ProcessManagement;

internal interface ITransferProcessDueWorkDispatcher
{
    Task<int> DispatchDueAsync(CancellationToken cancellationToken);
}

internal sealed class TransferProcessDueWorkDispatcher(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider) : ITransferProcessDueWorkDispatcher
{
    private const int BatchSize = 100;
    private static readonly TimeSpan ContentionDelay = TimeSpan.FromSeconds(1);

    public async Task<int> DispatchDueAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<DueTransferProcess> due;
        await using (var queryScope = scopeFactory.CreateAsyncScope())
        {
            var manager = queryScope.ServiceProvider.GetRequiredService<ITransferProcessManager>();
            due = await manager.GetDueAsync(timeProvider.GetUtcNow(), BatchSize, cancellationToken);
        }

        var dispatched = 0;
        foreach (var work in due)
        {
            await using var workScope = scopeFactory.CreateAsyncScope();
            if (work.NextAction != TransferProcessAction.ReserveBalance)
            {
                // TASK-07 has no executable continuation action. Persistently park
                // unsupported work so each poll does not rediscover it in a hot loop.
                var unsupportedManager = workScope.ServiceProvider.GetRequiredService<ITransferProcessManager>();
                await unsupportedManager.MarkWaitingAsync(
                    work.TransferId,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                continue;
            }

            var step = workScope.ServiceProvider.GetRequiredService<IReserveBalanceProcessStep>();
            var outcome = await step.ExecuteAsync(work.TransferId, cancellationToken);
            if (outcome == ReserveBalanceStepOutcome.RetryableContention)
            {
                var now = timeProvider.GetUtcNow();
                var manager = workScope.ServiceProvider.GetRequiredService<ITransferProcessManager>();
                await manager.RecordAttemptAsync(work.TransferId, now + ContentionDelay, now, cancellationToken);
            }

            dispatched++;
        }

        return dispatched;
    }
}
