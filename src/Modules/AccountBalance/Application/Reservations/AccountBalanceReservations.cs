using TransferOrchestration.AccountBalance.Application.Persistence;
using TransferOrchestration.AccountBalance.Contracts;
using TransferOrchestration.AccountBalance.Domain.Accounts;
using TransferOrchestration.BuildingBlocks.Domain;

namespace TransferOrchestration.AccountBalance.Application.Reservations;

internal sealed class AccountBalanceReservations(
    IAccountRepository repository,
    TimeProvider timeProvider,
    IReservationAttemptObserver observer,
    IReservationRetryDelay retryDelay) : IAccountBalanceReservations
{
    internal const int MaximumAttempts = 3;

    public async Task<ReserveFundsResult> ReserveAsync(
        ReserveFundsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsValidRequest(request))
        {
            return Result(ReserveFundsOutcome.InvalidAmount);
        }

        var currency = request.Currency.Trim().ToUpperInvariant();
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            var account = await repository.GetByIdAsync(
                new AccountId(request.SourceAccountId),
                request.TransferId,
                cancellationToken);
            if (account is null)
            {
                return Result(ReserveFundsOutcome.AccountNotFound);
            }

            // Currency is part of a valid reservation intent even though it is
            // canonically owned by Account rather than duplicated on Reservation.
            if (!string.Equals(account.Currency, currency, StringComparison.Ordinal))
            {
                return Result(ReserveFundsOutcome.CurrencyMismatch);
            }

            var existingIntent = await repository.GetReservationIntentAsync(
                request.TransferId,
                cancellationToken);
            if (existingIntent is not null && existingIntent.AccountId != request.SourceAccountId)
            {
                return Result(ReserveFundsOutcome.ConflictingReservation);
            }

            var existing = account.Reservations.SingleOrDefault(
                reservation => reservation.TransferId == request.TransferId);
            if (existing is not null)
            {
                return Result(existing.Amount == request.Amount
                    && existing.Status == BalanceReservationStatus.Active
                    ? ReserveFundsOutcome.AlreadyReserved
                    : ReserveFundsOutcome.ConflictingReservation);
            }

            if (account.Status != AccountStatus.Active)
            {
                return Result(ReserveFundsOutcome.AccountInactive);
            }

            if (account.AvailableBalance < request.Amount)
            {
                return Result(ReserveFundsOutcome.InsufficientBalance);
            }

            try
            {
                account.Reserve(request.TransferId, request.Amount, timeProvider.GetUtcNow());
                await observer.AfterAccountLoadedAsync(attempt, cancellationToken);
                await repository.SaveChangesAsync(cancellationToken);
                return Result(ReserveFundsOutcome.Succeeded);
            }
            catch (AccountConcurrencyConflictException) when (attempt < MaximumAttempts)
            {
                // The repository clears all losing tracked state. The next loop iteration
                // reloads PostgreSQL state and re-evaluates every invariant.
                await retryDelay.DelayAsync(attempt, cancellationToken);
            }
            catch (AccountConcurrencyConflictException)
            {
                return Result(ReserveFundsOutcome.ContentionRetryExhausted);
            }
            catch (ReservationTransferConflictException)
            {
                return await ClassifyUniqueConflictAsync(request, cancellationToken);
            }
            catch
            {
                repository.DiscardTrackedChanges();
                throw;
            }
        }

        return Result(ReserveFundsOutcome.ContentionRetryExhausted);
    }

    private async Task<ReserveFundsResult> ClassifyUniqueConflictAsync(
        ReserveFundsRequest request,
        CancellationToken cancellationToken)
    {
        var intent = await repository.GetReservationIntentAsync(request.TransferId, cancellationToken);
        var account = await repository.GetByIdAsync(
            new AccountId(request.SourceAccountId),
            request.TransferId,
            cancellationToken);

        if (account is null)
        {
            return Result(ReserveFundsOutcome.AccountNotFound);
        }

        var currency = request.Currency.Trim().ToUpperInvariant();
        if (!string.Equals(account.Currency, currency, StringComparison.Ordinal))
        {
            return Result(ReserveFundsOutcome.CurrencyMismatch);
        }

        return Result(intent is not null
            && intent.AccountId == request.SourceAccountId
            && intent.Amount == request.Amount
            && intent.Status == BalanceReservationStatus.Active
                ? ReserveFundsOutcome.AlreadyReserved
                : ReserveFundsOutcome.ConflictingReservation);
    }

    private static bool IsValidRequest(ReserveFundsRequest request)
    {
        if (request.TransferId == Guid.Empty || request.SourceAccountId == Guid.Empty
            || request.Amount <= 0 || string.IsNullOrWhiteSpace(request.Currency))
        {
            return false;
        }

        try
        {
            MonetaryAmountGuard.EnsureRepresentable(request.Amount, "Reservation amount");
            return request.Currency.Trim().Length == 3;
        }
        catch (DomainException)
        {
            return false;
        }
    }

    private static ReserveFundsResult Result(ReserveFundsOutcome outcome) => new(outcome);
}

internal interface IReservationAttemptObserver
{
    Task AfterAccountLoadedAsync(int attempt, CancellationToken cancellationToken);
}

internal sealed class NoOpReservationAttemptObserver : IReservationAttemptObserver
{
    public Task AfterAccountLoadedAsync(int attempt, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal interface IReservationRetryDelay
{
    Task DelayAsync(int failedAttempt, CancellationToken cancellationToken);
}

internal sealed class ReservationRetryDelay(TimeProvider timeProvider) : IReservationRetryDelay
{
    public Task DelayAsync(int failedAttempt, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(25 * failedAttempt), timeProvider, cancellationToken);
}
