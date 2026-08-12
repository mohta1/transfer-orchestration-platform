namespace TransferOrchestration.Notification.Infrastructure.Persistence;

internal sealed class ProcessedMessage
{
    private ProcessedMessage() { }

    internal ProcessedMessage(Guid messageId, string consumerName, DateTimeOffset processedAtUtc)
    {
        MessageId = messageId;
        ConsumerName = consumerName;
        ProcessedAtUtc = processedAtUtc;
    }

    internal ProcessedMessage(Guid messageId, string consumerName, Guid ownerId, DateTimeOffset claimedUntilUtc)
    {
        MessageId = messageId;
        ConsumerName = consumerName;
        OwnerId = ownerId;
        ClaimedUntilUtc = claimedUntilUtc;
    }

    public Guid MessageId { get; private set; }
    public string ConsumerName { get; private set; } = string.Empty;
    public DateTimeOffset? ProcessedAtUtc { get; private set; }
    public Guid? OwnerId { get; private set; }
    public DateTimeOffset? ClaimedUntilUtc { get; private set; }

    internal void Complete(DateTimeOffset processedAtUtc)
    {
        ProcessedAtUtc = processedAtUtc;
        OwnerId = null;
        ClaimedUntilUtc = null;
    }
}
