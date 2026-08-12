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

    public Guid MessageId { get; private set; }
    public string ConsumerName { get; private set; } = string.Empty;
    public DateTimeOffset ProcessedAtUtc { get; private set; }
}
