namespace TransferOrchestration.AccountBalance.Contracts;

public interface IAccountBalanceReservationFinalization
{
    Task<FinalizeFundsResult> ConsumeAsync(
        FinalizeFundsRequest request,
        CancellationToken cancellationToken);

    Task<FinalizeFundsResult> ReleaseAsync(
        FinalizeFundsRequest request,
        CancellationToken cancellationToken);
}

public sealed record FinalizeFundsRequest(
    Guid TransferId,
    Guid SourceAccountId);

public sealed record FinalizeFundsResult(FinalizeFundsOutcome Outcome)
{
    public bool IsSuccess => Outcome is FinalizeFundsOutcome.Succeeded
        or FinalizeFundsOutcome.AlreadyConsumed
        or FinalizeFundsOutcome.AlreadyReleased;
}

public enum FinalizeFundsOutcome
{
    Succeeded,
    AlreadyConsumed,
    AlreadyReleased,
    AccountNotFound,
    ReservationNotFound,
    ConflictingState,
    ContentionRetryExhausted
}
