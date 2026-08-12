using System.ComponentModel.DataAnnotations;

namespace TransferOrchestration.Notification.Infrastructure;

internal sealed class LoggingNotificationProviderOptions
{
    internal const string SectionName = "Notification:LoggingProvider";
    [Range(1, 100_000)] public int Capacity { get; init; } = 10_000;
    [Range(1, 86_400)] public int RetentionSeconds { get; init; } = 3_600;
    internal TimeSpan Retention => TimeSpan.FromSeconds(RetentionSeconds);
}
