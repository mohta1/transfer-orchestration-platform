using TransferOrchestration.BuildingBlocks.Domain;
using TransferOrchestration.TransferManagement.Domain.Transfers.Events;

namespace TransferOrchestration.TransferManagement.Domain.Transfers;

internal sealed class Transfer : AggregateRoot<TransferId>
{
    private Transfer(
        TransferId id,
        Guid sourceAccountId,
        Guid destinationAccountId,
        decimal amount,
        string currency,
        TransferType type,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        SourceAccountId = sourceAccountId;
        DestinationAccountId = destinationAccountId;
        Amount = amount;
        Currency = currency;
        Type = type;
        State = TransferState.Draft;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    private Transfer()
        : base(default)
    {
        Currency = string.Empty;
    }

    public Guid SourceAccountId { get; private set; }

    public Guid DestinationAccountId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public TransferType Type { get; private set; }

    public TransferState State { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Transfer Create(
        Guid sourceAccountId,
        Guid destinationAccountId,
        decimal amount,
        string currency,
        TransferType type,
        DateTimeOffset nowUtc)
    {
        if (sourceAccountId == Guid.Empty)
        {
            throw new DomainException("Source account is required.");
        }

        if (destinationAccountId == Guid.Empty)
        {
            throw new DomainException("Destination account is required.");
        }

        if (sourceAccountId == destinationAccountId)
        {
            throw new DomainException(
                "Source and destination accounts must be different.");
        }

        if (amount <= 0)
        {
            throw new DomainException(
                "Transfer amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new DomainException("Currency is required.");
        }

        var normalizedCurrency = currency.Trim().ToUpperInvariant();

        if (normalizedCurrency.Length != 3)
        {
            throw new DomainException(
                "Currency must be a three-letter code.");
        }

        return new Transfer(
            TransferId.New(),
            sourceAccountId,
            destinationAccountId,
            amount,
            normalizedCurrency,
            type,
            nowUtc);
    }

    public void Submit(DateTimeOffset nowUtc)
    {
        Transition(
            TransferState.Draft,
            TransferState.Submitted,
            nowUtc);
    }

    public void MarkValidationFailed(DateTimeOffset nowUtc)
    {
        Transition(
            TransferState.Submitted,
            TransferState.ValidationFailed,
            nowUtc);
    }

    public void RequestAuthorisation(DateTimeOffset nowUtc)
    {
        Transition(
            TransferState.Submitted,
            TransferState.PendingAuthorisation,
            nowUtc);
    }

    public void Authorise(DateTimeOffset nowUtc)
    {
        Transition(
            TransferState.PendingAuthorisation,
            TransferState.Authorised,
            nowUtc);
    }

    public void RejectAuthorisation(DateTimeOffset nowUtc)
    {
        Transition(
            TransferState.PendingAuthorisation,
            TransferState.Rejected,
            nowUtc);
    }

    public void BeginFraudScreening(DateTimeOffset nowUtc)
    {
        Transition(
            TransferState.Authorised,
            TransferState.PendingFraudScreening,
            nowUtc);
    }

    public void RejectForFraud(DateTimeOffset nowUtc)
    {
        Transition(
            TransferState.PendingFraudScreening,
            TransferState.FraudRejected,
            nowUtc);
    }

    public void RequestBalanceReservation(DateTimeOffset nowUtc)
    {
        Transition(
            TransferState.PendingFraudScreening,
            TransferState.PendingBalanceReservation,
            nowUtc);
    }

    public void MarkBalanceReserved(DateTimeOffset nowUtc)
    {
        Transition(
            TransferState.PendingBalanceReservation,
            TransferState.BalanceReserved,
            nowUtc);
    }

    public void BeginExternalSubmission(DateTimeOffset nowUtc)
    {
        if (Type != TransferType.DomesticInterbank)
        {
            throw new DomainException(
                "Only domestic interbank transfers are submitted to the external payment network.");
        }

        Transition(
            TransferState.BalanceReserved,
            TransferState.PendingExternalSubmission,
            nowUtc);
    }

    public void MarkSubmissionStatusUnknown(DateTimeOffset nowUtc)
    {
        Transition(
            TransferState.PendingExternalSubmission,
            TransferState.SubmissionStatusUnknown,
            nowUtc);
    }

    public void MarkSettlementPending(DateTimeOffset nowUtc)
    {
        if (State is not TransferState.PendingExternalSubmission
            and not TransferState.SubmissionStatusUnknown)
        {
            ThrowInvalidTransition(TransferState.SettlementPending);
        }

        SetState(TransferState.SettlementPending, nowUtc);
    }

    public void RejectExternalSubmission(DateTimeOffset nowUtc)
    {
        if (State is not TransferState.PendingExternalSubmission
            and not TransferState.SubmissionStatusUnknown)
        {
            ThrowInvalidTransition(TransferState.Rejected);
        }

        SetState(TransferState.Rejected, nowUtc);
    }

    public void CompleteSettlement(DateTimeOffset nowUtc)
    {
        Transition(
            TransferState.SettlementPending,
            TransferState.Completed,
            nowUtc);

        RaiseDomainEvent(
            new TransferCompletedDomainEvent(Id, nowUtc));
    }

    public void CompleteInternalTransfer(DateTimeOffset nowUtc)
    {
        if (Type != TransferType.InternalBank)
        {
            throw new DomainException(
                "Only internal bank transfers can complete directly after balance reservation.");
        }

        Transition(
            TransferState.BalanceReserved,
            TransferState.Completed,
            nowUtc);

        RaiseDomainEvent(
            new TransferCompletedDomainEvent(Id, nowUtc));
    }

    public void MarkCompensationRequired(DateTimeOffset nowUtc)
    {
        Transition(
            TransferState.SettlementPending,
            TransferState.CompensationRequired,
            nowUtc);
    }

    public void RequireManualReview(DateTimeOffset nowUtc)
    {
        if (State is not TransferState.SubmissionStatusUnknown
            and not TransferState.CompensationRequired)
        {
            ThrowInvalidTransition(
                TransferState.ManualReviewRequired);
        }

        SetState(
            TransferState.ManualReviewRequired,
            nowUtc);
    }

    private void Transition(
        TransferState expectedState,
        TransferState targetState,
        DateTimeOffset nowUtc)
    {
        if (State != expectedState)
        {
            ThrowInvalidTransition(targetState);
        }

        SetState(targetState, nowUtc);
    }

    private void SetState(
        TransferState targetState,
        DateTimeOffset nowUtc)
    {
        State = targetState;
        UpdatedAtUtc = nowUtc;
    }

    private void ThrowInvalidTransition(
        TransferState targetState)
    {
        throw new DomainException(
            $"Transfer cannot transition from '{State}' to '{targetState}'.");
    }
}
