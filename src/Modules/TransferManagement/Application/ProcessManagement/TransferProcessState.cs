using TransferOrchestration.BuildingBlocks.Domain;
using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.TransferManagement.Application.ProcessManagement;

internal sealed class TransferProcessState
{
    private TransferProcessState(
        TransferId transferId,
        Guid correlationId,
        DateTimeOffset nowUtc)
    {
        TransferId = transferId;
        CorrelationId = correlationId;
        Status = TransferProcessStatus.Active;
        CurrentStep = TransferProcessStep.Created;
        NextAction = TransferProcessAction.ContinueWorkflow;
        NextAttemptAtUtc = nowUtc;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    private TransferProcessState()
    {
    }

    public TransferId TransferId { get; private set; }

    public Guid CorrelationId { get; private set; }

    public TransferProcessStatus Status { get; private set; }

    public TransferProcessStep CurrentStep { get; private set; }

    public TransferProcessAction NextAction { get; private set; }

    public int AttemptCount { get; private set; }

    public string? NetworkSubmissionReference { get; private set; }

    public DateTimeOffset? NextAttemptAtUtc { get; private set; }

    public long Version { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static TransferProcessState Create(TransferId transferId, Guid correlationId, DateTimeOffset nowUtc)
    {
        if (transferId.Value == Guid.Empty)
        {
            throw new DomainException("Transfer identifier is required.");
        }

        if (correlationId == Guid.Empty)
        {
            throw new DomainException("Correlation identifier is required.");
        }

        EnsureUtc(nowUtc, nameof(nowUtc));
        return new TransferProcessState(transferId, correlationId, nowUtc);
    }

    public void Schedule(TransferProcessAction nextAction, DateTimeOffset nextAttemptAtUtc, DateTimeOffset nowUtc)
    {
        EnsureMutable();
        EnsureUpdateTime(nowUtc);
        EnsureUtc(nextAttemptAtUtc, nameof(nextAttemptAtUtc));
        if (nextAction == TransferProcessAction.None)
        {
            throw new DomainException("An actionable process requires a next action.");
        }

        if (nextAction == TransferProcessAction.SubmitToPaymentNetwork
            && NetworkSubmissionReference is not null)
        {
            throw new DomainException("External submission cannot be scheduled after its reference is assigned.");
        }

        if (nextAttemptAtUtc < nowUtc)
        {
            throw new DomainException("A newly scheduled attempt cannot be earlier than the update time.");
        }

        Status = TransferProcessStatus.Active;
        CurrentStep = TransferProcessStep.ActionScheduled;
        NextAction = nextAction;
        NextAttemptAtUtc = nextAttemptAtUtc;
        Touch(nowUtc);
    }

    public void RecordAttempt(DateTimeOffset nextAttemptAtUtc, DateTimeOffset nowUtc)
    {
        EnsureMutable();
        EnsureUpdateTime(nowUtc);
        EnsureUtc(nextAttemptAtUtc, nameof(nextAttemptAtUtc));
        if (Status != TransferProcessStatus.Active || NextAction == TransferProcessAction.None || NextAttemptAtUtc is null)
        {
            throw new DomainException("Only actionable process work can record an attempt.");
        }

        if (nextAttemptAtUtc < nowUtc)
        {
            throw new DomainException("The next attempt cannot be earlier than the attempt time.");
        }

        checked
        {
            AttemptCount++;
        }

        CurrentStep = TransferProcessStep.ActionScheduled;
        NextAttemptAtUtc = nextAttemptAtUtc;
        Touch(nowUtc);
    }

    public void Claim(DateTimeOffset leaseUntilUtc, DateTimeOffset nowUtc)
    {
        EnsureMutable();
        EnsureUpdateTime(nowUtc);
        EnsureUtc(leaseUntilUtc, nameof(leaseUntilUtc));
        if (Status != TransferProcessStatus.Active
            || NextAction == TransferProcessAction.None
            || NextAttemptAtUtc is null
            || NextAttemptAtUtc > nowUtc)
        {
            throw new DomainException("Only due actionable process work can be claimed.");
        }

        if (leaseUntilUtc <= nowUtc)
        {
            throw new DomainException("A process claim lease must expire after its claim time.");
        }

        NextAttemptAtUtc = leaseUntilUtc;
        Touch(nowUtc);
    }

    public void MarkWaiting(DateTimeOffset nowUtc)
    {
        EnsureMutable();
        EnsureUpdateTime(nowUtc);
        Status = TransferProcessStatus.Waiting;
        CurrentStep = TransferProcessStep.WaitingForOutcome;
        NextAction = TransferProcessAction.None;
        NextAttemptAtUtc = null;
        Touch(nowUtc);
    }

    public void PrepareExternalSubmission(string networkSubmissionReference, DateTimeOffset nowUtc)
    {
        EnsureMutable();
        EnsureUpdateTime(nowUtc);
        ArgumentException.ThrowIfNullOrWhiteSpace(networkSubmissionReference);

        if (NetworkSubmissionReference is not null
            && !string.Equals(NetworkSubmissionReference, networkSubmissionReference, StringComparison.Ordinal))
        {
            throw new DomainException("The network submission reference is immutable once assigned.");
        }

        if (Status != TransferProcessStatus.Active || NextAction != TransferProcessAction.SubmitToPaymentNetwork)
        {
            throw new DomainException("Only claimed payment submission work can be prepared.");
        }

        NetworkSubmissionReference ??= networkSubmissionReference;
        Status = TransferProcessStatus.Active;
        CurrentStep = TransferProcessStep.ActionScheduled;
        NextAction = TransferProcessAction.EnquirePaymentStatus;
        NextAttemptAtUtc = nowUtc;
        Touch(nowUtc);
    }

    public void Complete(DateTimeOffset nowUtc)
    {
        EnsureMutable();
        EnsureUpdateTime(nowUtc);
        Status = TransferProcessStatus.Completed;
        CurrentStep = TransferProcessStep.Completed;
        NextAction = TransferProcessAction.None;
        NextAttemptAtUtc = null;
        Touch(nowUtc);
    }

    private void EnsureMutable()
    {
        if (Status == TransferProcessStatus.Completed)
        {
            throw new DomainException("A completed process cannot return to active coordination.");
        }
    }

    private void EnsureUpdateTime(DateTimeOffset nowUtc)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        if (nowUtc < CreatedAtUtc || nowUtc < UpdatedAtUtc)
        {
            throw new DomainException("Process update time cannot move backwards.");
        }
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainException($"{parameterName} must be UTC.");
        }
    }

    private void Touch(DateTimeOffset nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        checked
        {
            Version++;
        }
    }
}
