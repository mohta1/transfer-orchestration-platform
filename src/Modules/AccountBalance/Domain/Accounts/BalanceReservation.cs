using TransferOrchestration.BuildingBlocks.Domain;

namespace TransferOrchestration.AccountBalance.Domain.Accounts;

internal sealed class BalanceReservation
    : Entity<BalanceReservationId>
{
    private BalanceReservation(
        BalanceReservationId id,
        Guid transferId,
        decimal amount,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        TransferId = transferId;
        Amount = amount;
        Status = BalanceReservationStatus.Active;
        CreatedAtUtc = createdAtUtc;
    }

    private BalanceReservation()
        : base(default)
    {
    }

    public Guid TransferId { get; private set; }

    public decimal Amount { get; private set; }

    public BalanceReservationStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? FinalisedAtUtc { get; private set; }

    public static BalanceReservation Create(
        Guid transferId,
        decimal amount,
        DateTimeOffset nowUtc)
    {
        if (transferId == Guid.Empty)
        {
            throw new DomainException("Transfer identifier is required.");
        }

        if (amount <= 0)
        {
            throw new DomainException(
                "Reservation amount must be greater than zero.");
        }

        return new BalanceReservation(
            BalanceReservationId.New(),
            transferId,
            amount,
            nowUtc);
    }

    public void Consume(DateTimeOffset nowUtc)
    {
        if (Status == BalanceReservationStatus.Consumed)
        {
            return;
        }

        if (Status == BalanceReservationStatus.Released)
        {
            throw new DomainException(
                "A released reservation cannot be consumed.");
        }

        Status = BalanceReservationStatus.Consumed;
        FinalisedAtUtc = nowUtc;
    }

    public void Release(DateTimeOffset nowUtc)
    {
        if (Status == BalanceReservationStatus.Released)
        {
            return;
        }

        if (Status == BalanceReservationStatus.Consumed)
        {
            throw new DomainException(
                "A consumed reservation cannot be released.");
        }

        Status = BalanceReservationStatus.Released;
        FinalisedAtUtc = nowUtc;
    }
}
