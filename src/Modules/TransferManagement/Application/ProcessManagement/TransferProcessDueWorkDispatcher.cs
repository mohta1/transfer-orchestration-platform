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
    internal const int MaximumContentionReschedules = 2;
    private static readonly TimeSpan ContentionDelay = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan ClaimLease = TimeSpan.FromSeconds(30);

    public async Task<int> DispatchDueAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<DueTransferProcess> due;
        await using (var queryScope = scopeFactory.CreateAsyncScope())
        {
            var manager = queryScope.ServiceProvider.GetRequiredService<ITransferProcessManager>();
            due = await manager.GetDueForActionAsync(
                TransferProcessAction.ReserveBalance,
                timeProvider.GetUtcNow(),
                BatchSize,
                cancellationToken);
        }

        var dispatched = 0;
        foreach (var work in due)
        {
            await using var workScope = scopeFactory.CreateAsyncScope();
            var manager = workScope.ServiceProvider.GetRequiredService<ITransferProcessManager>();
            var claimTime = timeProvider.GetUtcNow();
            var claimedVersion = await manager.TryClaimDueAsync(
                work.TransferId,
                TransferProcessAction.ReserveBalance,
                claimTime,
                claimTime + ClaimLease,
                cancellationToken);
            if (claimedVersion is null)
            {
                continue;
            }

            var step = workScope.ServiceProvider.GetRequiredService<IReserveBalanceProcessStep>();
            var outcome = await step.ExecuteAsync(work.TransferId, claimedVersion.Value, cancellationToken);
            if (outcome == ReserveBalanceStepOutcome.RetryableContention)
            {
                var now = timeProvider.GetUtcNow();
                if (work.AttemptCount >= MaximumContentionReschedules)
                {
                    await manager.MarkWaitingAsync(work.TransferId, claimedVersion.Value, now, cancellationToken);
                }
                else
                {
                    await manager.RecordAttemptAsync(work.TransferId, claimedVersion.Value, now + ContentionDelay, now, cancellationToken);
                }
            }

            dispatched++;
        }

        return dispatched;
    }
}
