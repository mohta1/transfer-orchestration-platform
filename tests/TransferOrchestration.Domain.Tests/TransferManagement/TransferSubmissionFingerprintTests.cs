using TransferOrchestration.TransferManagement.Application.Idempotency;
using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.Domain.Tests.TransferManagement;

public sealed class TransferSubmissionFingerprintTests
{
    private static readonly Guid SourceAccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DestinationAccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void FingerprintIsDeterministicAndCanonicalizesSemanticEquivalents()
    {
        var first = TransferSubmissionFingerprint.Create(Request(100m, " gbp "));
        var second = TransferSubmissionFingerprint.Create(Request(100.0000m, "GBP"));

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.Equal("2E37845FE7D73E1911D65B5409B5F50EE2D862DB82F6F9288CAED6FD775F7D69", first);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("source")]
    [InlineData("destination")]
    [InlineData("type")]
    public void EverySemanticallyRelevantFieldChangesFingerprint(string field)
    {
        var baseline = Request(100m, "GBP");
        var changed = field switch
        {
            "amount" => baseline with { Amount = 100.0001m },
            "currency" => baseline with { Currency = "EUR" },
            "source" => baseline with { SourceAccountId = Guid.NewGuid() },
            "destination" => baseline with { DestinationAccountId = Guid.NewGuid() },
            "type" => baseline with { Type = TransferType.DomesticInterbank },
            _ => throw new InvalidOperationException()
        };

        Assert.NotEqual(
            TransferSubmissionFingerprint.Create(baseline),
            TransferSubmissionFingerprint.Create(changed));
    }

    private static TransferSubmissionRequest Request(decimal amount, string currency) =>
        new(SourceAccountId, DestinationAccountId, amount, currency, TransferType.InternalBank);
}
