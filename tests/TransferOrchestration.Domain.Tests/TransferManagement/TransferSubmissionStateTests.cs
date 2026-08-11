using TransferOrchestration.BuildingBlocks.Domain;
using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.Domain.Tests.TransferManagement;

public sealed class TransferSubmissionStateTests
{
    [Fact]
    public void DailyLimitRejectionIsLegalOnlyAfterAuthorization()
    {
        var now = DateTimeOffset.UtcNow;
        var transfer = Transfer.Create(Guid.NewGuid(), Guid.NewGuid(), 10m, "GBP", TransferType.InternalBank, now);
        transfer.Submit(now);
        transfer.RequestAuthorisation(now);
        transfer.Authorise(now);

        transfer.RejectDailyLimit(now);

        Assert.Equal(TransferState.Rejected, transfer.State);
        Assert.Throws<DomainException>(() => transfer.BeginFraudScreening(now));
    }
}
