using TransferOrchestration.BuildingBlocks.Domain;

namespace TransferOrchestration.AccountBalance.Domain.Accounts;

internal sealed class Account : AggregateRoot<AccountId>
{
    private readonly List<BalanceReservation> _reservations = [];

    private Account(
        AccountId id,
        string currency,
        decimal availableBalance,
        AccountStatus status)
        : base(id)
    {
        Currency = currency;
        AvailableBalance = availableBalance;
        Status = status;
    }

    private Account()
        : base(default)
    {
        Currency = string.Empty;
    }

    public string Currency { get; private set; }

    public decimal AvailableBalance { get; private set; }

    public decimal ReservedBalance { get; private set; }

    public AccountStatus Status { get; private set; }

    public long Version { get; private set; }

    public IReadOnlyCollection<BalanceReservation> Reservations =>
        _reservations.AsReadOnly();

    public static Account Create(
        Guid id,
        string currency,
        decimal availableBalance,
        AccountStatus status = AccountStatus.Active)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("Account identifier is required.");
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new DomainException("Account currency is required.");
        }

        if (availableBalance < 0)
        {
            throw new DomainException(
                "Available balance cannot be negative.");
        }

        var normalizedCurrency =
            currency.Trim().ToUpperInvariant();

        if (normalizedCurrency.Length != 3)
        {
            throw new DomainException(
                "Currency must be a three-letter code.");
        }

        return new Account(
            new AccountId(id),
            normalizedCurrency,
            availableBalance,
            status);
    }

    public BalanceReservation Reserve(
        Guid transferId,
        decimal amount,
        DateTimeOffset nowUtc)
    {
        var existingReservation =
            _reservations.SingleOrDefault(
                reservation =>
                    reservation.TransferId == transferId);

        if (existingReservation is not null)
        {
            if (existingReservation.Amount != amount)
            {
                throw new DomainException(
                    "A reservation already exists for this transfer with a different amount.");
            }

            return existingReservation;
        }

        if (Status != AccountStatus.Active)
        {
            throw new DomainException(
                "Funds cannot be reserved on an inactive account.");
        }

        if (amount <= 0)
        {
            throw new DomainException(
                "Reservation amount must be greater than zero.");
        }

        if (AvailableBalance < amount)
        {
            throw new DomainException(
                "Insufficient available balance.");
        }

        var reservation =
            BalanceReservation.Create(
                transferId,
                amount,
                nowUtc);

        AvailableBalance -= amount;
        ReservedBalance += amount;

        _reservations.Add(reservation);

        IncrementVersion();
        EnsureBalanceInvariant();

        return reservation;
    }

    public void ConsumeReservation(
        Guid transferId,
        DateTimeOffset nowUtc)
    {
        var reservation =
            GetReservation(transferId);

        if (reservation.Status
            == BalanceReservationStatus.Consumed)
        {
            return;
        }

        if (reservation.Status
            == BalanceReservationStatus.Released)
        {
            throw new DomainException(
                "A released reservation cannot be consumed.");
        }

        ReservedBalance -= reservation.Amount;

        reservation.Consume(nowUtc);

        IncrementVersion();
        EnsureBalanceInvariant();
    }

    public void ReleaseReservation(
        Guid transferId,
        DateTimeOffset nowUtc)
    {
        var reservation =
            GetReservation(transferId);

        if (reservation.Status
            == BalanceReservationStatus.Released)
        {
            return;
        }

        if (reservation.Status
            == BalanceReservationStatus.Consumed)
        {
            throw new DomainException(
                "A consumed reservation cannot be released.");
        }

        ReservedBalance -= reservation.Amount;
        AvailableBalance += reservation.Amount;

        reservation.Release(nowUtc);

        IncrementVersion();
        EnsureBalanceInvariant();
    }

    private BalanceReservation GetReservation(
        Guid transferId)
    {
        return _reservations.SingleOrDefault(
                   reservation =>
                       reservation.TransferId == transferId)
               ?? throw new DomainException(
                   "Balance reservation was not found.");
    }

    private void IncrementVersion()
    {
        checked
        {
            Version++;
        }
    }

    private void EnsureBalanceInvariant()
    {
        if (AvailableBalance < 0)
        {
            throw new DomainException(
                "Available balance cannot become negative.");
        }

        if (ReservedBalance < 0)
        {
            throw new DomainException(
                "Reserved balance cannot become negative.");
        }
    }
}
