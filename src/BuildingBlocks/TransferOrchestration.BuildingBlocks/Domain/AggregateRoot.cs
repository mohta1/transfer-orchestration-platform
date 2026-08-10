namespace TransferOrchestration.BuildingBlocks.Domain;

public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id)
        : base(id)
    {
    }

    public IReadOnlyCollection<IDomainEvent> DomainEvents =>
        _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        _domainEvents.Add(domainEvent);
    }

    public IReadOnlyCollection<IDomainEvent> PullDomainEvents()
    {
        var domainEvents = _domainEvents.ToArray();

        _domainEvents.Clear();

        return domainEvents;
    }
}
