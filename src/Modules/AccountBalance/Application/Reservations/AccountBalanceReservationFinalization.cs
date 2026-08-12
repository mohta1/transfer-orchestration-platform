using TransferOrchestration.AccountBalance.Application.Persistence;
using TransferOrchestration.AccountBalance.Contracts;
using TransferOrchestration.AccountBalance.Domain.Accounts;

namespace TransferOrchestration.AccountBalance.Application.Reservations;

internal sealed class AccountBalanceReservationFinalization(
    IAccountRepository repository,
    TimeProvider timeProvider,
    IReservationRetryDelay retryDelay) : IAccountBalanceReservationFinalization
{
    internal const int MaximumAttempts = 3;

    public Task<FinalizeFundsResult> ConsumeAsync(
        FinalizeFundsRequest request,
        CancellationToken cancellationToken) =>
        FinalizeAsync(request, account => account.ConsumeReservation(request.TransferId, timeProvider.GetUtcNow()),
            FinalizeFundsOutcome.AlreadyConsumed, cancellationToken);

    public Task<FinalizeFundsResult> ReleaseAsync(
        FinalizeFundsRequest request,
        CancellationToken cancellationToken) =>
        FinalizeAsync(request, account => account.ReleaseReservation(request.TransferId, timeProvider.GetUtcNow()),
            FinalizeFundsOutcome.AlreadyReleased, cancellationToken);

    private async Task<FinalizeFundsResult> FinalizeAsync(
        FinalizeFundsRequest request,
        Action<Account> finalize,
        FinalizeFundsOutcome alreadyFinalizedOutcome,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TransferId == Guid.Empty || request.SourceAccountId == Guid.Empty)
        {
            return Result(FinalizeFundsOutcome.ConflictingState);
        }

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            var account = await repository.GetByIdAsync(
                new AccountId(request.SourceAccountId),
                request.TransferId,
                cancellationToken);
            if (account is null)
            {
                return Result(FinalizeFundsOutcome.AccountNotFound);
            }

            var reservation = account.Reservations.SingleOrDefault(
                item => item.TransferId == request.TransferId);
            if (reservation is null)
            {
                return Result(FinalizeFundsOutcome.ReservationNotFound);
            }

            if (alreadyFinalizedOutcome == FinalizeFundsOutcome.AlreadyConsumed
                && reservation.Status == BalanceReservationStatus.Consumed)
            {
                return Result(FinalizeFundsOutcome.AlreadyConsumed);
            }

            if (alreadyFinalizedOutcome == FinalizeFundsOutcome.AlreadyReleased
                && reservation.Status == BalanceReservationStatus.Released)
            {
                return Result(FinalizeFundsOutcome.AlreadyReleased);
            }

            if (reservation.Status != BalanceReservationStatus.Active)
            {
                return Result(FinalizeFundsOutcome.ConflictingState);
            }

            try
            {
                finalize(account);
                await repository.SaveChangesAsync(cancellationToken);
                return Result(FinalizeFundsOutcome.Succeeded);
            }
            catch (AccountConcurrencyConflictException) when (attempt < MaximumAttempts)
            {
                await retryDelay.DelayAsync(attempt, cancellationToken);
            }
            catch (AccountConcurrencyConflictException)
            {
                return Result(FinalizeFundsOutcome.ContentionRetryExhausted);
            }
            catch
            {
                repository.DiscardTrackedChanges();
                throw;
            }
        }

        return Result(FinalizeFundsOutcome.ContentionRetryExhausted);
    }

    private static FinalizeFundsResult Result(FinalizeFundsOutcome outcome) => new(outcome);
}
