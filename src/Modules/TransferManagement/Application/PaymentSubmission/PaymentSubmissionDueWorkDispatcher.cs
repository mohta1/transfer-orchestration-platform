using Microsoft.Extensions.DependencyInjection;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;

namespace TransferOrchestration.TransferManagement.Application.PaymentSubmission;

internal interface IPaymentSubmissionDueWorkDispatcher
{
    Task<int> DispatchDueAsync(CancellationToken cancellationToken);
}

internal sealed class PaymentSubmissionDueWorkDispatcher(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider) : IPaymentSubmissionDueWorkDispatcher
{
    internal static readonly TimeSpan ClaimLease = TimeSpan.FromSeconds(30);

    public async Task<int> DispatchDueAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<DueTransferProcess> due;
        await using (var queryScope = scopeFactory.CreateAsyncScope())
        {
            due = await queryScope.ServiceProvider.GetRequiredService<ITransferProcessManager>()
                .GetDueForActionAsync(TransferProcessAction.SubmitToPaymentNetwork, timeProvider.GetUtcNow(), 100, cancellationToken);
        }

        var dispatched = 0;
        foreach (var work in due)
        {
            await using var workScope = scopeFactory.CreateAsyncScope();
            var manager = workScope.ServiceProvider.GetRequiredService<ITransferProcessManager>();
            var now = timeProvider.GetUtcNow();
            var claim = await manager.TryClaimDueAsync(
                work.TransferId,
                TransferProcessAction.SubmitToPaymentNetwork,
                work.Version,
                now,
                now + ClaimLease,
                cancellationToken);
            if (claim is null)
            {
                continue;
            }

            await workScope.ServiceProvider.GetRequiredService<IPaymentSubmissionProcessStep>()
                .ExecuteAsync(work.TransferId, claim.ClaimedVersion, cancellationToken);
            dispatched++;
        }

        return dispatched;
    }
}
