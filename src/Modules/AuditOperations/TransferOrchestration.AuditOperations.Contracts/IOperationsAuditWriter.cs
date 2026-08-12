namespace TransferOrchestration.AuditOperations.Contracts;

public interface IOperationsAuditWriter
{
    Task<OperationsAuditEntry?> FindByCommandIdAsync(
        string commandId,
        CancellationToken cancellationToken);

    void Stage(OperationsAuditEntry entry);

    Task SaveStagedAsync(CancellationToken cancellationToken);

    void Enlist(object dbContextTransaction);
}

public sealed record OperationsAuditEntry(
    string CommandId,
    string ActorId,
    string Action,
    Guid TransferId,
    string PreviousState,
    string NewState,
    string Reason,
    Guid CorrelationId,
    Guid? CausationId,
    DateTimeOffset OccurredAtUtc);
