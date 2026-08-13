using TransferOrchestration.BuildingBlocks.Domain;
using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.Domain.Tests.TransferManagement;

public sealed class TransferFraudScreeningTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 13, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RejectForFraudTransitionsPendingFraudScreeningToFraudRejected()
    {
        var transfer = CreateAtPendingFraudScreening();

        transfer.RejectForFraud(Now.AddSeconds(1));

        Assert.Equal(TransferState.FraudRejected, transfer.State);
    }

    [Fact]
    public void FraudRejectedTransferCannotRequestBalanceReservation()
    {
        var transfer = CreateAtPendingFraudScreening();
        transfer.RejectForFraud(Now.AddSeconds(1));

        Assert.Throws<DomainException>(() => transfer.RequestBalanceReservation(Now.AddSeconds(2)));
    }

    [Fact]
    public void FraudRejectedTransferCannotBeginExternalSubmission()
    {
        var transfer = CreateAtPendingFraudScreening();
        transfer.RejectForFraud(Now.AddSeconds(1));

        Assert.Throws<DomainException>(() => transfer.BeginExternalSubmission(Now.AddSeconds(2)));
    }

    [Fact]
    public void FraudRejectedTransferCannotSettleOrComplete()
    {
        var transfer = CreateAtPendingFraudScreening();
        transfer.RejectForFraud(Now.AddSeconds(1));

        Assert.Throws<DomainException>(() => transfer.MarkSettlementPending(Now.AddSeconds(2)));
        Assert.Throws<DomainException>(() => transfer.CompleteSettlement(Now.AddSeconds(3)));
    }

    [Fact]
    public void InvalidRepeatedFraudDecisionsAreRejected()
    {
        var transfer = CreateAtPendingFraudScreening();
        transfer.RejectForFraud(Now.AddSeconds(1));

        Assert.Throws<DomainException>(() => transfer.RejectForFraud(Now.AddSeconds(2)));
        Assert.Throws<DomainException>(() => transfer.RequestBalanceReservation(Now.AddSeconds(3)));
    }

    [Fact]
    public void PendingFraudScreeningRemainsWithoutDefinitiveRejectionWhenNoTransitionIsApplied()
    {
        var transfer = CreateAtPendingFraudScreening();

        Assert.Equal(TransferState.PendingFraudScreening, transfer.State);
    }

    private static Transfer CreateAtPendingFraudScreening()
    {
        var transfer = Transfer.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            100m,
            "GBP",
            TransferType.DomesticInterbank,
            Now);
        transfer.Submit(Now);
        transfer.RequestAuthorisation(Now);
        transfer.Authorise(Now);
        transfer.BeginFraudScreening(Now);
        return transfer;
    }
}
