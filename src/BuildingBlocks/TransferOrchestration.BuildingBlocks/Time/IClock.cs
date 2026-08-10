namespace TransferOrchestration.BuildingBlocks.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
