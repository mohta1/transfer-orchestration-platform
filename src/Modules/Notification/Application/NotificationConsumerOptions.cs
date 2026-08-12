using System.ComponentModel.DataAnnotations;

namespace TransferOrchestration.Notification.Application;

internal sealed class NotificationConsumerOptions
{
    internal const string SectionName = "Notification:Consumer";
    [Range(1, 3600)] public int ClaimLeaseSeconds { get; init; } = 120;
    internal TimeSpan ClaimLease => TimeSpan.FromSeconds(ClaimLeaseSeconds);
}
