using TransferOrchestration.BuildingBlocks.Domain;
using TransferOrchestration.AccountBalance.Domain.Accounts;

namespace TransferOrchestration.Domain.Tests.AccountBalance;

public sealed class AccountTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReserveMovesFundsFromAvailableToReserved()
    {
        var account = CreateAccount(1000m);
        var transferId = Guid.NewGuid();

        var reservation =
            account.Reserve(
                transferId,
                300m,
                Now);

        Assert.Equal(700m, account.AvailableBalance);
        Assert.Equal(300m, account.ReservedBalance);
        Assert.Equal(
            BalanceReservationStatus.Active,
            reservation.Status);
    }

    [Fact]
    public void ReserveWithInsufficientBalanceThrowsDomainException()
    {
        var account = CreateAccount(100m);

        Assert.Throws<DomainException>(() =>
            account.Reserve(
                Guid.NewGuid(),
                101m,
                Now));

        Assert.Equal(100m, account.AvailableBalance);
        Assert.Equal(0m, account.ReservedBalance);
    }

    [Fact]
    public void ReserveOnInactiveAccountThrowsDomainException()
    {
        var account =
            Account.Create(
                Guid.NewGuid(),
                "EUR",
                1000m,
                AccountStatus.Inactive);

        Assert.Throws<DomainException>(() =>
            account.Reserve(
                Guid.NewGuid(),
                100m,
                Now));
    }

    [Fact]
    public void DuplicateReservationDoesNotReserveFundsTwice()
    {
        var account = CreateAccount(1000m);
        var transferId = Guid.NewGuid();

        var first =
            account.Reserve(
                transferId,
                300m,
                Now);

        var second =
            account.Reserve(
                transferId,
                300m,
                Now.AddSeconds(1));

        Assert.Same(first, second);
        Assert.Equal(700m, account.AvailableBalance);
        Assert.Equal(300m, account.ReservedBalance);
        Assert.Equal(1, account.Version);
        Assert.Equal(BalanceReservationStatus.Active, second.Status);
        Assert.Single(account.Reservations);
    }

    [Fact]
    public void DuplicateConsumedReservationReturnsExistingWithoutFinancialEffect()
    {
        var account = CreateAccount(1000m);
        var transferId = Guid.NewGuid();
        var reservation = account.Reserve(transferId, 300m, Now);

        account.ConsumeReservation(transferId, Now.AddSeconds(1));
        var versionAfterConsume = account.Version;

        var duplicate =
            account.Reserve(transferId, 300m, Now.AddSeconds(2));

        Assert.Same(reservation, duplicate);
        Assert.Equal(BalanceReservationStatus.Consumed, duplicate.Status);
        Assert.Equal(700m, account.AvailableBalance);
        Assert.Equal(0m, account.ReservedBalance);
        Assert.Equal(versionAfterConsume, account.Version);
        Assert.Single(account.Reservations);
    }

    [Fact]
    public void DuplicateReleasedReservationReturnsExistingWithoutFinancialEffect()
    {
        var account = CreateAccount(1000m);
        var transferId = Guid.NewGuid();
        var reservation = account.Reserve(transferId, 300m, Now);

        account.ReleaseReservation(transferId, Now.AddSeconds(1));
        var versionAfterRelease = account.Version;

        var duplicate =
            account.Reserve(transferId, 300m, Now.AddSeconds(2));

        Assert.Same(reservation, duplicate);
        Assert.Equal(BalanceReservationStatus.Released, duplicate.Status);
        Assert.Equal(1000m, account.AvailableBalance);
        Assert.Equal(0m, account.ReservedBalance);
        Assert.Equal(versionAfterRelease, account.Version);
        Assert.Single(account.Reservations);
    }

    [Fact]
    public void DuplicateReservationWithDifferentAmountThrowsDomainException()
    {
        var account = CreateAccount(1000m);
        var transferId = Guid.NewGuid();

        var reservation = account.Reserve(
            transferId,
            300m,
            Now);

        var versionBeforeDuplicate = account.Version;

        Assert.Throws<DomainException>(() =>
            account.Reserve(
                transferId,
                400m,
                Now.AddSeconds(1)));

        Assert.Equal(700m, account.AvailableBalance);
        Assert.Equal(300m, account.ReservedBalance);
        Assert.Equal(versionBeforeDuplicate, account.Version);
        Assert.Equal(BalanceReservationStatus.Active, reservation.Status);
        Assert.Single(account.Reservations);
    }

    [Fact]
    public void ConsumeReservationRemovesReservedFunds()
    {
        var account = CreateAccount(1000m);
        var transferId = Guid.NewGuid();

        var reservation =
            account.Reserve(
                transferId,
                300m,
                Now);

        account.ConsumeReservation(
            transferId,
            Now.AddSeconds(1));

        Assert.Equal(700m, account.AvailableBalance);
        Assert.Equal(0m, account.ReservedBalance);
        Assert.Equal(
            BalanceReservationStatus.Consumed,
            reservation.Status);
    }

    [Fact]
    public void DuplicateConsumeDoesNotApplyFinancialEffectTwice()
    {
        var account = CreateAccount(1000m);
        var transferId = Guid.NewGuid();

        account.Reserve(
            transferId,
            300m,
            Now);

        account.ConsumeReservation(
            transferId,
            Now.AddSeconds(1));

        account.ConsumeReservation(
            transferId,
            Now.AddSeconds(2));

        Assert.Equal(700m, account.AvailableBalance);
        Assert.Equal(0m, account.ReservedBalance);
    }

    [Fact]
    public void ReleaseReservationReturnsFundsToAvailableBalance()
    {
        var account = CreateAccount(1000m);
        var transferId = Guid.NewGuid();

        var reservation =
            account.Reserve(
                transferId,
                300m,
                Now);

        account.ReleaseReservation(
            transferId,
            Now.AddSeconds(1));

        Assert.Equal(1000m, account.AvailableBalance);
        Assert.Equal(0m, account.ReservedBalance);
        Assert.Equal(
            BalanceReservationStatus.Released,
            reservation.Status);
    }

    [Fact]
    public void DuplicateReleaseDoesNotApplyFinancialEffectTwice()
    {
        var account = CreateAccount(1000m);
        var transferId = Guid.NewGuid();

        account.Reserve(
            transferId,
            300m,
            Now);

        account.ReleaseReservation(
            transferId,
            Now.AddSeconds(1));

        account.ReleaseReservation(
            transferId,
            Now.AddSeconds(2));

        Assert.Equal(1000m, account.AvailableBalance);
        Assert.Equal(0m, account.ReservedBalance);
    }

    [Fact]
    public void ReleasedReservationCannotBeConsumed()
    {
        var account = CreateAccount(1000m);
        var transferId = Guid.NewGuid();

        account.Reserve(
            transferId,
            300m,
            Now);

        account.ReleaseReservation(
            transferId,
            Now.AddSeconds(1));

        Assert.Throws<DomainException>(() =>
            account.ConsumeReservation(
                transferId,
                Now.AddSeconds(2)));
    }

    [Fact]
    public void ConsumedReservationCannotBeReleased()
    {
        var account = CreateAccount(1000m);
        var transferId = Guid.NewGuid();

        account.Reserve(
            transferId,
            300m,
            Now);

        account.ConsumeReservation(
            transferId,
            Now.AddSeconds(1));

        Assert.Throws<DomainException>(() =>
            account.ReleaseReservation(
                transferId,
                Now.AddSeconds(2)));
    }

    [Fact]
    public void CreateWithNegativeOpeningBalanceThrowsDomainException()
    {
        Assert.Throws<DomainException>(() =>
            CreateAccount(-1m));
    }

    private static Account CreateAccount(
        decimal availableBalance)
    {
        return Account.Create(
            Guid.NewGuid(),
            "EUR",
            availableBalance);
    }
}
