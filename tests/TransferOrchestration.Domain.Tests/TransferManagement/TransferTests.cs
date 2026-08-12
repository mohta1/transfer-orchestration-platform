using TransferOrchestration.BuildingBlocks.Domain;
using TransferOrchestration.TransferManagement.Domain.Transfers;
using TransferOrchestration.TransferManagement.Domain.Transfers.Events;

namespace TransferOrchestration.Domain.Tests.TransferManagement;

public sealed class TransferTests
{
    [Fact]
    public void PendingBalanceReservationCanBeExplicitlyRejected()
    {
        var now = DateTimeOffset.UtcNow;
        var transfer = Transfer.Create(Guid.NewGuid(), Guid.NewGuid(), 10m, "GBP", TransferType.DomesticInterbank, now);
        transfer.Submit(now);
        transfer.RequestAuthorisation(now);
        transfer.Authorise(now);
        transfer.BeginFraudScreening(now);
        transfer.RequestBalanceReservation(now);

        transfer.RejectBalanceReservation(now);

        Assert.Equal(TransferState.Rejected, transfer.State);
    }

    private static readonly DateTimeOffset Now =
        new(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateWithPositiveAmountCreatesDraftTransfer()
    {
        var transfer = CreateTransfer(amount: 100m);

        Assert.Equal(100m, transfer.Amount);
        Assert.Equal(TransferState.Draft, transfer.State);
    }

    [Fact]
    public void CreateWithZeroAmountThrowsDomainException()
    {
        Assert.Throws<DomainException>(() =>
            CreateTransfer(amount: 0m));
    }

    [Fact]
    public void CreateWithNegativeAmountThrowsDomainException()
    {
        Assert.Throws<DomainException>(() =>
            CreateTransfer(amount: -1m));
    }

    [Fact]
    public void CreateWithMoreThanFourFractionalDigitsThrowsDomainException()
    {
        Assert.Throws<DomainException>(() =>
            CreateTransfer(amount: 1.23456m));
    }

    [Fact]
    public void CreateWithFourDecimalAmountPreservesAmount()
    {
        var transfer = CreateTransfer(amount: 1.23400m);

        Assert.Equal(1.23400m, transfer.Amount);
    }

    [Fact]
    public void CreateWithSameSourceAndDestinationThrowsDomainException()
    {
        var accountId = Guid.NewGuid();

        Assert.Throws<DomainException>(() =>
            Transfer.Create(
                accountId,
                accountId,
                100m,
                "EUR",
                TransferType.DomesticInterbank,
                Now));
    }

    [Fact]
    public void SubmitFromDraftChangesStateToSubmitted()
    {
        var transfer = CreateTransfer();

        transfer.Submit(Now.AddSeconds(1));

        Assert.Equal(TransferState.Submitted, transfer.State);
    }

    [Fact]
    public void SubmitWhenAlreadySubmittedThrowsDomainException()
    {
        var transfer = CreateTransfer();

        transfer.Submit(Now.AddSeconds(1));

        Assert.Throws<DomainException>(() =>
            transfer.Submit(Now.AddSeconds(2)));
    }

	[Fact]
	public void ExternalSubmissionBeforeBalanceReservationThrowsDomainException()
	{
		var transfer = CreateTransfer();

		transfer.Submit(Now.AddSeconds(1));
		transfer.RequestAuthorisation(Now.AddSeconds(2));
		transfer.Authorise(Now.AddSeconds(3));
		transfer.BeginFraudScreening(Now.AddSeconds(4));

		Assert.Throws<DomainException>(() =>
			transfer.BeginExternalSubmission(Now.AddSeconds(5)));
	}

	[Fact]
	public void PaymentTimeoutMovesTransferToSubmissionStatusUnknown()
	{
		var transfer = CreateDomesticTransferReadyForExternalSubmission();

		transfer.BeginExternalSubmission(Now.AddSeconds(6));
		transfer.MarkSubmissionStatusUnknown(Now.AddSeconds(7));

		Assert.Equal(
			TransferState.SubmissionStatusUnknown,
			transfer.State);
	}

	[Fact]
	public void SettlementCanBeConfirmedAfterSubmissionStatusUnknown()
	{
		var transfer = CreateDomesticTransferReadyForExternalSubmission();

		transfer.BeginExternalSubmission(Now.AddSeconds(6));
		transfer.MarkSubmissionStatusUnknown(Now.AddSeconds(7));
		transfer.MarkSettlementPending(Now.AddSeconds(8));

		Assert.Equal(
			TransferState.SettlementPending,
			transfer.State);
	}

	[Fact]
	public void CompletedTransferCannotBeCompletedAgain()
	{
		var transfer = CreateDomesticTransferReadyForExternalSubmission();

		transfer.BeginExternalSubmission(Now.AddSeconds(6));
		transfer.MarkSettlementPending(Now.AddSeconds(7));
		transfer.CompleteSettlement(Now.AddSeconds(8));

		Assert.Throws<DomainException>(() =>
			transfer.CompleteSettlement(Now.AddSeconds(9)));
	}

	[Fact]
	public void CompletingTransferRaisesTransferCompletedDomainEvent()
	{
		var transfer = CreateDomesticTransferReadyForExternalSubmission();

		transfer.BeginExternalSubmission(Now.AddSeconds(6));
		transfer.MarkSettlementPending(Now.AddSeconds(7));
		transfer.CompleteSettlement(Now.AddSeconds(8));

		var domainEvent =
			Assert.Single(transfer.DomainEvents);

		Assert.IsType<TransferCompletedDomainEvent>(
			domainEvent);
	}

	[Fact]
	public void InternalTransferCannotBeSubmittedToExternalNetwork()
	{
		var transfer = Transfer.Create(
			Guid.NewGuid(),
			Guid.NewGuid(),
			100m,
			"EUR",
			TransferType.InternalBank,
			Now);

		transfer.Submit(Now.AddSeconds(1));
		transfer.RequestAuthorisation(Now.AddSeconds(2));
		transfer.Authorise(Now.AddSeconds(3));
		transfer.BeginFraudScreening(Now.AddSeconds(4));
		transfer.RequestBalanceReservation(Now.AddSeconds(5));
		transfer.MarkBalanceReserved(Now.AddSeconds(6));

		Assert.Throws<DomainException>(() =>
			transfer.BeginExternalSubmission(Now.AddSeconds(7)));
	}

    private static Transfer CreateDomesticTransferReadyForExternalSubmission()
    {
        var transfer = CreateTransfer();

        transfer.Submit(Now.AddSeconds(1));
        transfer.RequestAuthorisation(Now.AddSeconds(2));
        transfer.Authorise(Now.AddSeconds(3));
        transfer.BeginFraudScreening(Now.AddSeconds(4));
        transfer.RequestBalanceReservation(Now.AddSeconds(5));
        transfer.MarkBalanceReserved(Now.AddSeconds(6));

        return transfer;
    }

    [Fact]
    public void ManualReviewRequiredCanBeRejectedManually()
    {
        var transfer = CreateDomesticTransferReadyForExternalSubmission();
        transfer.BeginExternalSubmission(Now.AddSeconds(7));
        transfer.MarkSubmissionStatusUnknown(Now.AddSeconds(8));
        transfer.RequireManualReview(Now.AddSeconds(9));

        transfer.RejectManually(Now.AddSeconds(10));

        Assert.Equal(TransferState.Rejected, transfer.State);
    }

    [Fact]
    public void ManualReviewRequiredCanBeConfirmedSettledManually()
    {
        var transfer = CreateDomesticTransferReadyForExternalSubmission();
        transfer.BeginExternalSubmission(Now.AddSeconds(7));
        transfer.MarkSubmissionStatusUnknown(Now.AddSeconds(8));
        transfer.RequireManualReview(Now.AddSeconds(9));

        transfer.ConfirmSettlementManually(Now.AddSeconds(10));

        Assert.Equal(TransferState.Completed, transfer.State);
        Assert.Contains(transfer.DomainEvents, item => item is TransferCompletedDomainEvent);
    }

    [Fact]
    public void RejectManuallyFromInvalidStateThrowsDomainException()
    {
        var transfer = CreateTransfer();
        transfer.Submit(Now.AddSeconds(1));

        Assert.Throws<DomainException>(() => transfer.RejectManually(Now.AddSeconds(2)));
    }

    private static Transfer CreateTransfer(decimal amount = 100m)
    {
        return Transfer.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            amount,
            "EUR",
            TransferType.DomesticInterbank,
            Now);
    }
}
