namespace TransferOrchestration.AuditOperations.Domain;

internal sealed class OperationsAuditRecord
{
    internal const int MaxActorIdLength = 200;
    internal const int MaxActionLength = 100;
    internal const int MaxStateLength = 100;
    internal const int MaxReasonLength = 1000;
    internal const int MaxCommandIdLength = 200;

    private OperationsAuditRecord()
    {
        ActorId = string.Empty;
        Action = string.Empty;
        PreviousState = string.Empty;
        NewState = string.Empty;
        Reason = string.Empty;
        CommandId = string.Empty;
    }

    public long Id { get; private set; }

    public string CommandId { get; private set; }

    public string ActorId { get; private set; }

    public string Action { get; private set; }

    public Guid TransferId { get; private set; }

    public string PreviousState { get; private set; }

    public string NewState { get; private set; }

    public string Reason { get; private set; }

    public Guid CorrelationId { get; private set; }

    public Guid? CausationId { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public static OperationsAuditRecord Create(
        string commandId,
        string actorId,
        string action,
        Guid transferId,
        string previousState,
        string newState,
        string reason,
        Guid correlationId,
        Guid? causationId,
        DateTimeOffset occurredAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(previousState);
        ArgumentException.ThrowIfNullOrWhiteSpace(newState);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (transferId == Guid.Empty)
        {
            throw new ArgumentException("Transfer identifier is required.", nameof(transferId));
        }

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("Correlation identifier is required.", nameof(correlationId));
        }

        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Audit timestamp must be UTC.", nameof(occurredAtUtc));
        }

        return new OperationsAuditRecord
        {
            CommandId = Truncate(commandId, MaxCommandIdLength),
            ActorId = Truncate(actorId, MaxActorIdLength),
            Action = Truncate(action, MaxActionLength),
            TransferId = transferId,
            PreviousState = Truncate(previousState, MaxStateLength),
            NewState = Truncate(newState, MaxStateLength),
            Reason = Truncate(reason, MaxReasonLength),
            CorrelationId = correlationId,
            CausationId = causationId,
            OccurredAtUtc = occurredAtUtc
        };
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
