using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Application.Reconciliation;
using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.TransferManagement.Application.Queries;

internal static class StuckTransferClassifier
{
    internal static readonly HashSet<TransferState> TerminalStates =
    [
        TransferState.Completed,
        TransferState.Rejected,
        TransferState.Cancelled,
        TransferState.FraudRejected
    ];

    internal static readonly HashSet<TransferState> EligibleWorkflowStates =
    [
        TransferState.Submitted,
        TransferState.PendingAuthorisation,
        TransferState.Authorised,
        TransferState.PendingFraudScreening,
        TransferState.PendingBalanceReservation,
        TransferState.BalanceReserved,
        TransferState.PendingExternalSubmission,
        TransferState.SubmissionStatusUnknown,
        TransferState.SettlementPending,
        TransferState.CompensationRequired,
        TransferState.ManualReviewRequired
    ];

    public static bool IsEligibleTransferState(TransferState state) =>
        EligibleWorkflowStates.Contains(state);

    public static bool IsFutureScheduledProcessWork(
        TransferProcessStatus processStatus,
        DateTimeOffset? nextAttemptAtUtc,
        DateTimeOffset nowUtc) =>
        processStatus == TransferProcessStatus.Active
        && nextAttemptAtUtc is not null
        && nextAttemptAtUtc.Value > nowUtc;

    public static bool IsFutureScheduledReconciliationWork(
        TransferState transferState,
        ReconciliationStatus? reconciliationStatus,
        DateTimeOffset? reconciliationNextAttemptAtUtc,
        DateTimeOffset nowUtc) =>
        transferState == TransferState.SubmissionStatusUnknown
        && reconciliationStatus == ReconciliationStatus.Active
        && reconciliationNextAttemptAtUtc is not null
        && reconciliationNextAttemptAtUtc.Value > nowUtc;

    public static DateTimeOffset ReferenceTimestamp(
        DateTimeOffset transferUpdatedAtUtc,
        DateTimeOffset processUpdatedAtUtc) =>
        transferUpdatedAtUtc >= processUpdatedAtUtc
            ? transferUpdatedAtUtc
            : processUpdatedAtUtc;

    public static bool HasCrossedThreshold(
        DateTimeOffset referenceTimestamp,
        DateTimeOffset nowUtc,
        TimeSpan threshold) =>
        nowUtc - referenceTimestamp >= threshold;

    public static DateTimeOffset ThresholdCrossedAtUtc(
        DateTimeOffset referenceTimestamp,
        TimeSpan threshold) =>
        referenceTimestamp + threshold;

    public static string ClassifyCategory(
        TransferState transferState,
        TransferProcessStatus processStatus,
        DateTimeOffset? nextAttemptAtUtc,
        DateTimeOffset nowUtc)
    {
        if (transferState == TransferState.ManualReviewRequired)
        {
            return "ManualReviewRequired";
        }

        if (transferState == TransferState.SubmissionStatusUnknown)
        {
            return "SubmissionStatusUnknown";
        }

        if (processStatus == TransferProcessStatus.Waiting)
        {
            return "WaitingForOutcome";
        }

        if (processStatus == TransferProcessStatus.Active
            && nextAttemptAtUtc is not null
            && nextAttemptAtUtc.Value <= nowUtc)
        {
            return "OverdueScheduledWork";
        }

        return "StaleWorkflowState";
    }
}
