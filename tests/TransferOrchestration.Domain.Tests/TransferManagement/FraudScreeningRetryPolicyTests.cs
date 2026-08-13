using TransferOrchestration.BuildingBlocks.Domain;
using TransferOrchestration.TransferManagement.Application.FraudScreening;
using TransferOrchestration.TransferManagement.Infrastructure.FraudScreening;

namespace TransferOrchestration.Domain.Tests.TransferManagement;

public sealed class FraudScreeningRetryPolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    private static readonly FraudScreeningOptions DefaultOptions = new()
    {
        MaxTransientAttempts = 3,
        InitialRetryDelaySeconds = 2,
        MaxRetryDelaySeconds = 60
    };

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    public void BackoffCalculationUsesBoundedExponentialDelay(int attemptCount, int expectedDelaySeconds)
    {
        var nextAttempt = FraudScreeningRetryPolicy.CalculateNextAttempt(
            DefaultOptions,
            attemptCount,
            Now);

        Assert.Equal(Now.AddSeconds(expectedDelaySeconds), nextAttempt);
    }

    [Fact]
    public void BackoffCalculationCapsAtConfiguredMaximumDelay()
    {
        var options = new FraudScreeningOptions
        {
            MaxTransientAttempts = 3,
            InitialRetryDelaySeconds = 30,
            MaxRetryDelaySeconds = 45
        };
        var nextAttempt = FraudScreeningRetryPolicy.CalculateNextAttempt(options, 5, Now);

        Assert.Equal(Now.AddSeconds(45), nextAttempt);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    public void EscalationBoundaryUsesMaximumTransientAttempts(int attemptCount, bool shouldEscalate)
    {
        Assert.Equal(shouldEscalate, FraudScreeningRetryPolicy.ShouldEscalate(DefaultOptions, attemptCount));
    }

    [Fact]
    public void RetrySchedulingRejectsNonUtcTimestamps()
    {
        var localNow = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.FromHours(1));

        Assert.Throws<DomainException>(() =>
            FraudScreeningRetryPolicy.CalculateNextAttempt(DefaultOptions, 1, localNow));
    }

    [Fact]
    public void FraudScreeningResultIncludesRequiredSemanticOutcomes()
    {
        var expected = new[]
        {
            FraudScreeningResult.Approved,
            FraudScreeningResult.Rejected,
            FraudScreeningResult.ManualReviewRequired,
            FraudScreeningResult.Timeout,
            FraudScreeningResult.TemporarilyUnavailable
        };

        Assert.Equal(expected.Length, Enum.GetValues<FraudScreeningResult>().Length);
        Assert.All(expected, value => Assert.True(Enum.IsDefined(value)));
    }
}
