using Microsoft.EntityFrameworkCore;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Domain.Transfers;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence.Idempotency;
using TransferOrchestration.TransferManagement.Contracts.IntegrationEvents;
using TransferOrchestration.TransferManagement.Domain.Transfers.Events;
using TransferOrchestration.TransferManagement.Infrastructure.Outbox;
using System.Text.Json;

namespace TransferOrchestration.TransferManagement.Infrastructure.Persistence;

public sealed class TransferManagementDbContext(
    DbContextOptions<TransferManagementDbContext> options)
    : DbContext(options)
{
    public const string Schema = "transfer_management";

    internal DbSet<Transfer> Transfers => Set<Transfer>();

    internal DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    internal DbSet<TransferProcessState> TransferProcessStates => Set<TransferProcessState>();

    internal DbSet<DailyTransferUsage> DailyTransferUsages => Set<DailyTransferUsage>();

    internal DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AddOutboxMessages();
        var result = base.SaveChanges(acceptAllChangesOnSuccess);
        ClearDomainEvents();
        return result;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AddOutboxMessages();
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        ClearDomainEvents();
        return result;
    }

    private void AddOutboxMessages()
    {
        var trackedMessageIds = ChangeTracker.Entries<OutboxMessage>()
            .Select(entry => entry.Entity.MessageId)
            .ToHashSet();

        foreach (var aggregate in ChangeTracker.Entries<Transfer>().Select(entry => entry.Entity))
        {
            foreach (var domainEvent in aggregate.DomainEvents.OfType<TransferCompletedDomainEvent>())
            {
                if (!trackedMessageIds.Add(domainEvent.Id)) continue;

                var integrationEvent = new TransferCompletedIntegrationEvent(
                    domainEvent.Id, domainEvent.TransferId.Value, domainEvent.OccurredOnUtc);
                OutboxMessages.Add(new OutboxMessage(
                    integrationEvent.MessageId,
                    integrationEvent.TransferId,
                    TransferCompletedIntegrationEvent.EventType,
                    JsonSerializer.Serialize(integrationEvent),
                    domainEvent.OccurredOnUtc));
            }
        }
    }

    private void ClearDomainEvents()
    {
        foreach (var aggregate in ChangeTracker.Entries<Transfer>().Select(entry => entry.Entity))
        {
            aggregate.PullDomainEvents();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(TransferManagementDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
