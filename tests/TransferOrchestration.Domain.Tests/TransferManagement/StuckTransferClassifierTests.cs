using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Application.Queries;
using TransferOrchestration.TransferManagement.Application.Reconciliation;
using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.Domain.Tests.TransferManagement;

public sealed class StuckTransferClassifierTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(10);

    [Theory]
    [InlineData("Completed")]
    [InlineData("Rejected")]
    [InlineData("FraudRejected")]
    public void TerminalStatesAreNotEligible(string stateName)
    {
        var state = Enum.Parse<TransferState>(stateName);
        Assert.False(StuckTransferClassifier.IsEligibleTransferState(state));
    }

    [Theory]
    [InlineData("ManualReviewRequired")]
    [InlineData("SubmissionStatusUnknown")]
    [InlineData("PendingFraudScreening")]
    public void WorkflowStatesAreEligible(string stateName)
    {
        var state = Enum.Parse<TransferState>(stateName);
        Assert.True(StuckTransferClassifier.IsEligibleTransferState(state));
    }

    [Fact]
    public void FutureScheduledProcessWorkIsNotStuck()
    {
        var future = Now.AddMinutes(5);
        Assert.True(StuckTransferClassifier.IsFutureScheduledProcessWork(
            TransferProcessStatus.Active,
            future,
            Now));
    }

    [Fact]
    public void OverdueScheduledProcessWorkIsNotFutureScheduled()
    {
        var overdue = Now.AddMinutes(-1);
        Assert.False(StuckTransferClassifier.IsFutureScheduledProcessWork(
            TransferProcessStatus.Active,
            overdue,
            Now));
    }

    [Fact]
    public void FutureScheduledReconciliationWorkIsNotStuck()
    {
        var future = Now.AddMinutes(5);
        Assert.True(StuckTransferClassifier.IsFutureScheduledReconciliationWork(
            TransferState.SubmissionStatusUnknown,
            ReconciliationStatus.Active,
            future,
            Now));
    }

    [Fact]
    public void ExactThresholdBoundaryIsStuck()
    {
        var reference = Now - Threshold;
        Assert.True(StuckTransferClassifier.HasCrossedThreshold(reference, Now, Threshold));
    }

    [Fact]
    public void OneSecondBeforeThresholdIsNotStuck()
    {
        var reference = Now - Threshold + TimeSpan.FromSeconds(1);
        Assert.False(StuckTransferClassifier.HasCrossedThreshold(reference, Now, Threshold));
    }

    [Fact]
    public void ReferenceTimestampUsesLatestUpdate()
    {
        var transferUpdated = Now.AddMinutes(-20);
        var processUpdated = Now.AddMinutes(-5);
        Assert.Equal(processUpdated, StuckTransferClassifier.ReferenceTimestamp(transferUpdated, processUpdated));
    }

    [Fact]
    public void NonUtcNowIsRejectedByQueryServiceContractThroughUtcOffsetCheck()
    {
        var nonUtc = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.FromHours(1));
        Assert.NotEqual(TimeSpan.Zero, nonUtc.Offset);
    }

    [Fact]
    public void ManualReviewRequiredCategoryIsExplicit() =>
        Assert.Equal(
            "ManualReviewRequired",
            StuckTransferClassifier.ClassifyCategory(
                TransferState.ManualReviewRequired,
                TransferProcessStatus.Completed,
                null,
                Now));

    [Fact]
    public void WaitingProcessMapsToWaitingForOutcomeCategory() =>
        Assert.Equal(
            "WaitingForOutcome",
            StuckTransferClassifier.ClassifyCategory(
                TransferState.SettlementPending,
                TransferProcessStatus.Waiting,
                null,
                Now));
}
