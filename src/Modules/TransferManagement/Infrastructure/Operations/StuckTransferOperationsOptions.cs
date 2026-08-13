using System.ComponentModel.DataAnnotations;

namespace TransferOrchestration.TransferManagement.Infrastructure.Operations;

internal sealed class StuckTransferOperationsOptions
{
    internal const string SectionName = "TransferManagement:StuckTransfers";

    [Range(1, 86_400)]
    public int StateAgeThresholdSeconds { get; init; } = 600;

    [Range(1, 100)]
    public int MaxResults { get; init; } = 50;

    internal TimeSpan StateAgeThreshold => TimeSpan.FromSeconds(StateAgeThresholdSeconds);
}
