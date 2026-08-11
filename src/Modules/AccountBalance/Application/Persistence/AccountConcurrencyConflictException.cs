namespace TransferOrchestration.AccountBalance.Application.Persistence;

internal sealed class AccountConcurrencyConflictException : Exception
{
    public AccountConcurrencyConflictException(Guid accountId, Exception innerException)
        : base($"Account '{accountId}' was changed by another operation. Reload it and re-evaluate the financial invariants before retrying.", innerException)
    {
        AccountId = accountId;
    }

    public Guid AccountId { get; }
}
