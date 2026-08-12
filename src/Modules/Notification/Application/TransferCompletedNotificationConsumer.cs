using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TransferOrchestration.Notification.Contracts;
using TransferOrchestration.Notification.Infrastructure.Persistence;
using TransferOrchestration.TransferManagement.Contracts.IntegrationEvents;

namespace TransferOrchestration.Notification.Application;

internal sealed class TransferCompletedNotificationConsumer(
    NotificationDbContext dbContext,
    INotificationProvider provider,
    TimeProvider timeProvider,
    IOptions<NotificationConsumerOptions> options) : IIntegrationEventDispatcher
{
    internal const string ConsumerName = "notification.transfer-completed.v1";
    private readonly string _consumerName = ConsumerName;

    internal TransferCompletedNotificationConsumer(NotificationDbContext dbContext, INotificationProvider provider,
        TimeProvider timeProvider) : this(dbContext, provider, timeProvider,
            Options.Create(new NotificationConsumerOptions())) { }

    internal TransferCompletedNotificationConsumer(NotificationDbContext dbContext, INotificationProvider provider,
        TimeProvider timeProvider, string consumerName, IOptions<NotificationConsumerOptions>? options = null)
        : this(dbContext, provider, timeProvider, options ?? Options.Create(new NotificationConsumerOptions()))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        _consumerName = consumerName;
    }

    public async Task DispatchAsync(TransferCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var ownerId = Guid.NewGuid();
        var claim = await ClaimAsync(integrationEvent.MessageId, ownerId, cancellationToken);
        if (claim == ClaimResult.Completed) return;
        if (claim == ClaimResult.OwnedByAnother)
            throw new NotificationDeliveryInProgressException(integrationEvent.MessageId, _consumerName);

        var providerKey = new NotificationIdempotencyKey(_consumerName, integrationEvent.MessageId);
        try
        {
            // ClaimAsync closes its connection before this external call begins.
            await provider.NotifyTransferCompletedAsync(providerKey, integrationEvent, cancellationToken);
            await CompleteAsync(integrationEvent.MessageId, ownerId, cancellationToken);
        }
        catch
        {
            await ReleaseAsync(integrationEvent.MessageId, ownerId, CancellationToken.None);
            throw;
        }
    }

    private async Task<ClaimResult> ClaimAsync(Guid messageId, Guid ownerId, CancellationToken token)
    {
        var now = timeProvider.GetUtcNow();
        var affected = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notification.processed_messages
                (message_id, consumer_name, processed_at_utc, owner_id, claimed_until_utc)
            VALUES ({messageId}, {_consumerName}, NULL, {ownerId}, {now + options.Value.ClaimLease})
            ON CONFLICT (message_id, consumer_name) DO UPDATE
            SET owner_id = EXCLUDED.owner_id, claimed_until_utc = EXCLUDED.claimed_until_utc
            WHERE processed_messages.processed_at_utc IS NULL
              AND processed_messages.claimed_until_utc <= {now}
            """, token);
        if (affected == 1) return ClaimResult.Acquired;
        var completed = await dbContext.ProcessedMessages.AsNoTracking().AnyAsync(message =>
            message.MessageId == messageId && message.ConsumerName == _consumerName
                && message.ProcessedAtUtc != null, token);
        return completed ? ClaimResult.Completed : ClaimResult.OwnedByAnother;
    }

    private async Task CompleteAsync(Guid messageId, Guid ownerId, CancellationToken token)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(token);
        var message = await dbContext.ProcessedMessages.SingleOrDefaultAsync(item => item.MessageId == messageId
            && item.ConsumerName == _consumerName && item.OwnerId == ownerId, token)
            ?? throw new InvalidOperationException("The notification delivery claim was lost.");
        message.Complete(timeProvider.GetUtcNow());
        try
        {
            await dbContext.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        catch
        {
            dbContext.Entry(message).State = EntityState.Detached;
            throw;
        }
    }

    private Task<int> ReleaseAsync(Guid messageId, Guid ownerId, CancellationToken token) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM notification.processed_messages
            WHERE message_id = {messageId} AND consumer_name = {_consumerName}
              AND owner_id = {ownerId} AND processed_at_utc IS NULL
            """, token);

    private enum ClaimResult { Acquired, Completed, OwnedByAnother }
}

internal sealed class NotificationDeliveryInProgressException(Guid messageId, string consumerName)
    : Exception($"Notification delivery {messageId:D} is already in progress for {consumerName}.");
