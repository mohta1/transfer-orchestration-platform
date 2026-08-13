using TransferOrchestration.BuildingBlocks.Domain;
using TransferOrchestration.TransferManagement.Infrastructure.FraudScreening;

namespace TransferOrchestration.TransferManagement.Application.FraudScreening;

internal static class FraudScreeningRetryPolicy
{
    internal static DateTimeOffset CalculateNextAttempt(
        FraudScreeningOptions options,
        int attemptCountAfterFailure,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureUtc(nowUtc);

        if (attemptCountAfterFailure <= 0)
        {
            throw new DomainException("Attempt count must be positive when scheduling a fraud retry.");
        }

        var exponent = Math.Max(0, attemptCountAfterFailure - 1);
        var delaySeconds = (long)Math.Min(
            options.InitialRetryDelaySeconds * Math.Pow(2, exponent),
            options.MaxRetryDelaySeconds);

        return nowUtc.AddSeconds(delaySeconds);
    }

    internal static bool ShouldEscalate(FraudScreeningOptions options, int attemptCountAfterFailure) =>
        attemptCountAfterFailure >= options.MaxTransientAttempts;

    private static void EnsureUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new DomainException("Fraud retry scheduling time must be UTC.");
        }
    }
}
