using System.ComponentModel.DataAnnotations;

namespace TransferOrchestration.TransferManagement.Infrastructure.FraudScreening;

internal sealed class FraudScreeningOptions
{
    internal const string SectionName = "TransferManagement:FraudScreening";

    [Range(1, 100)]
    public int MaxTransientAttempts { get; init; } = 3;

    [Range(1, 3_600)]
    public int InitialRetryDelaySeconds { get; init; } = 2;

    [Range(1, 86_400)]
    public int MaxRetryDelaySeconds { get; init; } = 60;

    [Range(1, 3_600)]
    public int LeaseDurationSeconds { get; init; } = 30;

    internal TimeSpan LeaseDuration => TimeSpan.FromSeconds(LeaseDurationSeconds);
}
