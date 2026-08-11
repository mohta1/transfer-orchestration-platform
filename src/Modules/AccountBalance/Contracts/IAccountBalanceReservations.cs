namespace TransferOrchestration.AccountBalance.Contracts;

public interface IAccountBalanceReservations
{
    Task<ReserveFundsResult> ReserveAsync(
        ReserveFundsRequest request,
        CancellationToken cancellationToken);
}

public sealed record ReserveFundsRequest(
    Guid TransferId,
    Guid SourceAccountId,
    decimal Amount,
    string Currency);

public sealed record ReserveFundsResult(ReserveFundsOutcome Outcome)
{
    public bool IsSuccess => Outcome is ReserveFundsOutcome.Succeeded
        or ReserveFundsOutcome.AlreadyReserved;
}

public enum ReserveFundsOutcome
{
    Succeeded,
    AlreadyReserved,
    AccountNotFound,
    AccountInactive,
    CurrencyMismatch,
    InvalidAmount,
    InsufficientBalance,
    ConflictingReservation,
    ContentionRetryExhausted
}
