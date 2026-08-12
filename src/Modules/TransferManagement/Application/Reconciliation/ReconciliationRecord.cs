using TransferOrchestration.BuildingBlocks.Domain;
using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.TransferManagement.Application.Reconciliation;

internal sealed class ReconciliationRecord
{
    internal const int MaxLastErrorLength = 512;

    private ReconciliationRecord(
        TransferId transferId,
        string networkSubmissionReference,
        DateTimeOffset nowUtc)
    {
        TransferId = transferId;
        NetworkSubmissionReference = networkSubmissionReference;
        Status = ReconciliationStatus.Active;
        AttemptCount = 0;
        NextAttemptAtUtc = nowUtc;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    private ReconciliationRecord()
    {
        NetworkSubmissionReference = string.Empty;
    }

    public long Id { get; private set; }

    public TransferId TransferId { get; private set; }

    public string NetworkSubmissionReference { get; private set; }

    public ReconciliationStatus Status { get; private set; }

    public int AttemptCount { get; private set; }

    public DateTimeOffset? NextAttemptAtUtc { get; private set; }

    public DateTimeOffset? LastAttemptAtUtc { get; private set; }

    public string? LastEnquiryResult { get; private set; }

    public string? LastError { get; private set; }

    public string? LockedBy { get; private set; }

    public DateTimeOffset? LockedUntilUtc { get; private set; }

    public long Version { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static ReconciliationRecord ScheduleForUnknown(
        TransferId transferId,
        string networkSubmissionReference,
        DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(networkSubmissionReference);
        EnsureUtc(nowUtc);
        return new ReconciliationRecord(transferId, networkSubmissionReference, nowUtc);
    }

    public void RecordUnknownAttempt(
        DateTimeOffset nextAttemptAtUtc,
        string enquiryResult,
        DateTimeOffset nowUtc)
    {
        EnsureActive();
        EnsureUpdateTime(nowUtc);
        EnsureUtc(nextAttemptAtUtc, nameof(nextAttemptAtUtc));
        if (nextAttemptAtUtc < nowUtc)
        {
            throw new DomainException("The next reconciliation attempt cannot be earlier than the update time.");
        }

        checked
        {
            AttemptCount++;
        }

        LastAttemptAtUtc = nowUtc;
        LastEnquiryResult = enquiryResult;
        LastError = null;
        NextAttemptAtUtc = nextAttemptAtUtc;
        ReleaseClaim(nowUtc);
        Touch(nowUtc);
    }

    public void EscalateToManualReview(DateTimeOffset nowUtc)
    {
        EnsureActive();
        EnsureUpdateTime(nowUtc);
        Status = ReconciliationStatus.ManualReviewRequired;
        NextAttemptAtUtc = null;
        LastError = null;
        ReleaseClaim(nowUtc);
        Touch(nowUtc);
    }

    public void Close(DateTimeOffset nowUtc, string enquiryResult)
    {
        EnsureActive();
        EnsureUpdateTime(nowUtc);
        Status = ReconciliationStatus.Closed;
        LastAttemptAtUtc = nowUtc;
        LastEnquiryResult = enquiryResult;
        LastError = null;
        NextAttemptAtUtc = null;
        ReleaseClaim(nowUtc);
        Touch(nowUtc);
    }

    public void RecordEnquiryFailure(DateTimeOffset nextAttemptAtUtc, string error, DateTimeOffset nowUtc)
    {
        EnsureActive();
        EnsureUpdateTime(nowUtc);
        EnsureUtc(nextAttemptAtUtc, nameof(nextAttemptAtUtc));
        if (nextAttemptAtUtc < nowUtc)
        {
            throw new DomainException("The next reconciliation attempt cannot be earlier than the update time.");
        }

        checked
        {
            AttemptCount++;
        }

        LastAttemptAtUtc = nowUtc;
        LastError = TruncateError(error);
        NextAttemptAtUtc = nextAttemptAtUtc;
        ReleaseClaim(nowUtc);
        Touch(nowUtc);
    }

    public void ApplyClaim(string workerId, DateTimeOffset leaseUntilUtc, DateTimeOffset nowUtc)
    {
        EnsureActive();
        EnsureUpdateTime(nowUtc);
        EnsureUtc(leaseUntilUtc, nameof(leaseUntilUtc));
        if (NextAttemptAtUtc is null || NextAttemptAtUtc > nowUtc)
        {
            throw new DomainException("Only due reconciliation work can be claimed.");
        }

        if (leaseUntilUtc <= nowUtc)
        {
            throw new DomainException("A reconciliation claim lease must expire after its claim time.");
        }

        LockedBy = workerId;
        LockedUntilUtc = leaseUntilUtc;
        Touch(nowUtc);
    }

    public void ReleaseClaim(DateTimeOffset nowUtc)
    {
        EnsureUpdateTime(nowUtc);
        LockedBy = null;
        LockedUntilUtc = null;
        Touch(nowUtc);
    }

    private void EnsureActive()
    {
        if (Status != ReconciliationStatus.Active)
        {
            throw new DomainException("Only active reconciliation records can be updated.");
        }
    }

    private void EnsureUpdateTime(DateTimeOffset nowUtc)
    {
        EnsureUtc(nowUtc);
        if (nowUtc < CreatedAtUtc || nowUtc < UpdatedAtUtc)
        {
            throw new DomainException("Reconciliation update time cannot move backwards.");
        }
    }

    private static string TruncateError(string error) =>
        error.Length <= MaxLastErrorLength ? error : error[..MaxLastErrorLength];

    private static void EnsureUtc(DateTimeOffset value, string? parameterName = null)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainException(
                parameterName is null ? "Time must be UTC." : $"{parameterName} must be UTC.");
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
