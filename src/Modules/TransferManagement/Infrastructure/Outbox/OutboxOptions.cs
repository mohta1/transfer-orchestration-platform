using System.ComponentModel.DataAnnotations;

namespace TransferOrchestration.TransferManagement.Infrastructure.Outbox;

internal sealed class OutboxOptions
{
    internal const string SectionName = "TransferManagement:Outbox";

    [Range(1, 60_000)] public int PollIntervalMilliseconds { get; init; } = 1_000;
    [Range(1, 100)] public int BatchSize { get; init; } = 20;
    [Range(1, 3_600)] public int LeaseDurationSeconds { get; init; } = 30;
    [Range(1, 100)] public int MaxAttempts { get; init; } = 8;
    [Range(1, 86_400)] public int InitialRetryDelaySeconds { get; init; } = 5;
    [Range(1, 86_400)] public int MaxRetryDelaySeconds { get; init; } = 900;

    internal TimeSpan PollInterval => TimeSpan.FromMilliseconds(PollIntervalMilliseconds);
    internal TimeSpan LeaseDuration => TimeSpan.FromSeconds(LeaseDurationSeconds);
    internal TimeSpan InitialRetryDelay => TimeSpan.FromSeconds(InitialRetryDelaySeconds);
    internal TimeSpan MaxRetryDelay => TimeSpan.FromSeconds(MaxRetryDelaySeconds);
}
