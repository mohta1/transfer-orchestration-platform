using Microsoft.EntityFrameworkCore;
using TransferOrchestration.Notification.Contracts;
using TransferOrchestration.Notification.Infrastructure.Persistence;
using TransferOrchestration.TransferManagement.Contracts.IntegrationEvents;

namespace TransferOrchestration.Notification.Application;

internal sealed class TransferCompletedNotificationConsumer(
    NotificationDbContext dbContext,
    INotificationProvider provider,
    TimeProvider timeProvider) : IIntegrationEventDispatcher
{
    internal const string ConsumerName = "notification.transfer-completed.v1";
    private readonly string _consumerName = ConsumerName;

    internal TransferCompletedNotificationConsumer(
        NotificationDbContext dbContext,
        INotificationProvider provider,
        TimeProvider timeProvider,
        string consumerName) : this(dbContext, provider, timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        _consumerName = consumerName;
    }

    public async Task DispatchAsync(
        TransferCompletedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // A transaction-scoped PostgreSQL advisory lock serializes this stable consumer/message pair.
        // The durable primary key remains the final uniqueness boundary.
        var lockKey = $"{_consumerName}:{integrationEvent.MessageId:D}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);

        if (await dbContext.ProcessedMessages.AsNoTracking().AnyAsync(
                message => message.MessageId == integrationEvent.MessageId
                    && message.ConsumerName == _consumerName,
                cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        await provider.NotifyTransferCompletedAsync(integrationEvent, cancellationToken);
        dbContext.ProcessedMessages.Add(new ProcessedMessage(
            integrationEvent.MessageId, _consumerName, timeProvider.GetUtcNow()));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
