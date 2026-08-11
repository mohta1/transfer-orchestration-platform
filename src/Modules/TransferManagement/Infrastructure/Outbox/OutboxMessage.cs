namespace TransferOrchestration.TransferManagement.Infrastructure.Outbox;

internal sealed class OutboxMessage
{
    private OutboxMessage() { }

    internal OutboxMessage(Guid messageId, Guid transferId, string type, string payload, DateTimeOffset occurredAtUtc)
    {
        MessageId = messageId;
        TransferId = transferId;
        Type = type;
        Payload = payload;
        OccurredAtUtc = occurredAtUtc;
        NextAttemptAtUtc = occurredAtUtc;
    }

    public long Id { get; private set; }
    public Guid MessageId { get; private set; }
    public Guid TransferId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public OutboxStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAtUtc { get; private set; }
    public string? LockedBy { get; private set; }
    public DateTimeOffset? LockedUntilUtc { get; private set; }
    public DateTimeOffset? PublishedAtUtc { get; private set; }
    public string? LastError { get; private set; }
}
