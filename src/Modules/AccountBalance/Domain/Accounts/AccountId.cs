namespace TransferOrchestration.AccountBalance.Domain.Accounts;

internal readonly record struct AccountId(Guid Value)
{
    public override string ToString() => Value.ToString();
}
