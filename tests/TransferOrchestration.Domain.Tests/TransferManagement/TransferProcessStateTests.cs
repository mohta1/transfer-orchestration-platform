using TransferOrchestration.BuildingBlocks.Domain;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.Domain.Tests.TransferManagement;

public sealed class TransferProcessStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreationRequiresIdentifiersAndUtcTimestamp()
    {
        Assert.Throws<DomainException>(() => TransferProcessState.Create(new TransferId(Guid.Empty), Guid.NewGuid(), Now));
        Assert.Throws<DomainException>(() => TransferProcessState.Create(TransferId.New(), Guid.Empty, Now));
        Assert.Throws<DomainException>(() => TransferProcessState.Create(TransferId.New(), Guid.NewGuid(), Now.ToOffset(TimeSpan.FromHours(1))));
    }

    [Fact]
    public void InitialProcessIsImmediatelyDurableWork()
    {
        var transferId = TransferId.New();
        var correlationId = Guid.NewGuid();

        var state = TransferProcessState.Create(transferId, correlationId, Now);

        Assert.Equal(transferId, state.TransferId);
        Assert.Equal(correlationId, state.CorrelationId);
        Assert.Equal(TransferProcessStatus.Active, state.Status);
        Assert.Equal(TransferProcessStep.Created, state.CurrentStep);
        Assert.Equal(TransferProcessAction.ContinueWorkflow, state.NextAction);
        Assert.Equal(0, state.AttemptCount);
        Assert.Equal(Now, state.NextAttemptAtUtc);
    }

    [Fact]
    public void InvalidUpdateDoesNotCorruptState()
    {
        var state = TransferProcessState.Create(TransferId.New(), Guid.NewGuid(), Now);

        Assert.Throws<DomainException>(() => state.Schedule(
            TransferProcessAction.None,
            Now.AddMinutes(1),
            Now));

        Assert.Equal(TransferProcessStatus.Active, state.Status);
        Assert.Equal(TransferProcessStep.Created, state.CurrentStep);
        Assert.Equal(TransferProcessAction.ContinueWorkflow, state.NextAction);
        Assert.Equal(Now, state.NextAttemptAtUtc);
        Assert.Equal(0, state.Version);
    }

    [Fact]
    public void CompletedProcessCannotReturnToActiveWork()
    {
        var state = TransferProcessState.Create(TransferId.New(), Guid.NewGuid(), Now);
        state.Complete(Now.AddMinutes(1));

        Assert.Throws<DomainException>(() => state.Schedule(
            TransferProcessAction.ContinueWorkflow,
            Now.AddMinutes(3),
            Now.AddMinutes(2)));

        Assert.Equal(TransferProcessStatus.Completed, state.Status);
        Assert.Equal(TransferProcessAction.None, state.NextAction);
        Assert.Null(state.NextAttemptAtUtc);
    }

    [Fact]
    public void AttemptMetadataOnlyAdvancesForActionableWork()
    {
        var state = TransferProcessState.Create(TransferId.New(), Guid.NewGuid(), Now);
        state.RecordAttempt(Now.AddMinutes(5), Now.AddMinutes(1));

        Assert.Equal(1, state.AttemptCount);
        Assert.Equal(Now.AddMinutes(5), state.NextAttemptAtUtc);

        state.MarkWaiting(Now.AddMinutes(2));
        Assert.Throws<DomainException>(() => state.RecordAttempt(Now.AddMinutes(7), Now.AddMinutes(3)));
        Assert.Equal(1, state.AttemptCount);
    }

    [Fact]
    public void ClaimLeasesDueWorkWithoutConsumingAttemptBudget()
    {
        var state = TransferProcessState.Create(TransferId.New(), Guid.NewGuid(), Now);
        state.Schedule(TransferProcessAction.ReserveBalance, Now, Now);

        state.Claim(Now.AddSeconds(30), Now);

        Assert.Equal(0, state.AttemptCount);
        Assert.Equal(TransferProcessAction.ReserveBalance, state.NextAction);
        Assert.Equal(Now.AddSeconds(30), state.NextAttemptAtUtc);
        Assert.Equal(2, state.Version);
        Assert.Throws<DomainException>(() => state.Claim(Now.AddSeconds(31), Now));
    }
}
