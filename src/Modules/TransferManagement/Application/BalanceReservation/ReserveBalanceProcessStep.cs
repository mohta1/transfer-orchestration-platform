using TransferOrchestration.AccountBalance.Contracts;
using TransferOrchestration.TransferManagement.Application.Persistence;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.TransferManagement.Application.BalanceReservation;

internal interface IReserveBalanceProcessStep
{
    Task<ReserveBalanceStepOutcome> ExecuteAsync(
        TransferId transferId,
        CancellationToken cancellationToken);
}

internal enum ReserveBalanceStepOutcome
{
    BalanceReserved,
    AlreadyCompleted,
    TransferRejected,
    RetryableContention,
    NotActionable
}

internal sealed class ReserveBalanceProcessStep(
    ITransferRepository transferRepository,
    ITransferProcessStateRepository processRepository,
    IAccountBalanceReservations accountBalanceReservations,
    TimeProvider timeProvider) : IReserveBalanceProcessStep
{
    public async Task<ReserveBalanceStepOutcome> ExecuteAsync(
        TransferId transferId,
        CancellationToken cancellationToken)
    {
        var transfer = await transferRepository.GetByIdAsync(transferId, cancellationToken)
            ?? throw new InvalidOperationException($"Transfer '{transferId.Value}' was not found.");
        var process = await processRepository.GetAsync(transferId, cancellationToken)
            ?? throw new InvalidOperationException($"Transfer process '{transferId.Value}' was not found.");

        if (transfer.State == TransferState.BalanceReserved
            && process.NextAction != TransferProcessAction.ReserveBalance)
        {
            return ReserveBalanceStepOutcome.AlreadyCompleted;
        }

        if (transfer.State != TransferState.PendingBalanceReservation
            || process.Status != TransferProcessStatus.Active
            || process.NextAction != TransferProcessAction.ReserveBalance)
        {
            return ReserveBalanceStepOutcome.NotActionable;
        }

        var result = await accountBalanceReservations.ReserveAsync(
            new ReserveFundsRequest(
                transfer.Id.Value,
                transfer.SourceAccountId,
                transfer.Amount,
                transfer.Currency),
            cancellationToken);

        if (result.IsSuccess)
        {
            var now = timeProvider.GetUtcNow();
            transfer.MarkBalanceReserved(now);
            process.Schedule(TransferProcessAction.ContinueWorkflow, now, now);
            await processRepository.SaveChangesAsync(cancellationToken);
            return ReserveBalanceStepOutcome.BalanceReserved;
        }

        if (result.Outcome == ReserveFundsOutcome.ContentionRetryExhausted)
        {
            return ReserveBalanceStepOutcome.RetryableContention;
        }

        var rejectionTime = timeProvider.GetUtcNow();
        transfer.RejectBalanceReservation(rejectionTime);
        process.Complete(rejectionTime);
        await processRepository.SaveChangesAsync(cancellationToken);
        return ReserveBalanceStepOutcome.TransferRejected;
    }
}
