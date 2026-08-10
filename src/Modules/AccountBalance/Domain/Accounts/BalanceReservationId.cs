namespace TransferOrchestration.AccountBalance.Domain.Accounts;

internal readonly record struct BalanceReservationId(Guid Value)
{
    public static BalanceReservationId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
