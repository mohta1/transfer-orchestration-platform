using TransferOrchestration.BuildingBlocks.Domain;

namespace TransferOrchestration.TransferManagement.Domain.Transfers.Events;

internal sealed record TransferCompletedDomainEvent(
    TransferId TransferId,
    DateTimeOffset OccurredOnUtc) : IDomainEvent
{
    public Guid Id { get; } = Guid.NewGuid();
}
