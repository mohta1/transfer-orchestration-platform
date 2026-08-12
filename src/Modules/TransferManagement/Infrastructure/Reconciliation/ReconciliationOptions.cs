using System.ComponentModel.DataAnnotations;

namespace TransferOrchestration.TransferManagement.Infrastructure.Reconciliation;

internal sealed class ReconciliationOptions
{
    internal const string SectionName = "TransferManagement:Reconciliation";

    [Range(1, 60_000)]
    public int PollIntervalMilliseconds { get; init; } = 1_000;

    [Range(1, 100)]
    public int BatchSize { get; init; } = 20;

    [Range(1, 3_600)]
    public int LeaseDurationSeconds { get; init; } = 30;

    [Range(1, 86_400)]
    public int RetryDelaySeconds { get; init; } = 5;

    [Range(1, 100)]
    public int EscalationAttemptThreshold { get; init; } = 5;

    internal TimeSpan PollInterval => TimeSpan.FromMilliseconds(PollIntervalMilliseconds);
    internal TimeSpan LeaseDuration => TimeSpan.FromSeconds(LeaseDurationSeconds);
    internal TimeSpan RetryDelay => TimeSpan.FromSeconds(RetryDelaySeconds);
}
