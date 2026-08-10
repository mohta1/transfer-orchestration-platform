namespace TransferOrchestration.BuildingBlocks.Domain;

public interface IDomainEvent
{
    Guid Id { get; }

    DateTimeOffset OccurredOnUtc { get; }
}
