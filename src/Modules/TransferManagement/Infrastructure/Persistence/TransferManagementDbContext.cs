using Microsoft.EntityFrameworkCore;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Domain.Transfers;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence.Idempotency;
using TransferOrchestration.TransferManagement.Contracts.IntegrationEvents;
using TransferOrchestration.TransferManagement.Domain.Transfers.Events;
using TransferOrchestration.TransferManagement.Application.Reconciliation;
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

    internal DbSet<ReconciliationRecord> ReconciliationRecords => Set<ReconciliationRecord>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AddOutboxMessages();
        var result = base.SaveChanges(acceptAllChangesOnSuccess);
        ClearDomainEvents();
        return result;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await AddOutboxMessagesAsync(cancellationToken);
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        ClearDomainEvents();
        return result;
    }

    private void AddOutboxMessages()
    {
        foreach (var (domainEvent, correlationId) in PendingCompletionEvents()
            .Select(item => (item, ResolveCorrelationId(item.TransferId))))
            AddOutboxMessage(domainEvent, correlationId);
    }

    private async Task AddOutboxMessagesAsync(CancellationToken token)
    {
        foreach (var domainEvent in PendingCompletionEvents())
            AddOutboxMessage(domainEvent, await ResolveCorrelationIdAsync(domainEvent.TransferId, token));
    }

    private TransferCompletedDomainEvent[] PendingCompletionEvents()
    {
        var trackedMessageIds = ChangeTracker.Entries<OutboxMessage>()
            .Select(entry => entry.Entity.MessageId)
            .ToHashSet();
        return ChangeTracker.Entries<Transfer>().SelectMany(entry => entry.Entity.DomainEvents)
            .OfType<TransferCompletedDomainEvent>().Where(item => trackedMessageIds.Add(item.Id)).ToArray();
    }

    private Guid? ResolveCorrelationId(TransferId transferId) =>
        TrackedCorrelationId(transferId) ?? TransferProcessStates.AsNoTracking()
            .Where(item => item.TransferId == transferId).Select(item => (Guid?)item.CorrelationId).SingleOrDefault();

    private async Task<Guid?> ResolveCorrelationIdAsync(TransferId transferId, CancellationToken token) =>
        TrackedCorrelationId(transferId) ?? await TransferProcessStates.AsNoTracking()
            .Where(item => item.TransferId == transferId).Select(item => (Guid?)item.CorrelationId).SingleOrDefaultAsync(token);

    private Guid? TrackedCorrelationId(TransferId transferId) => ChangeTracker.Entries<TransferProcessState>()
        .Where(entry => entry.State != EntityState.Deleted && entry.Entity.TransferId == transferId)
        .Select(entry => (Guid?)entry.Entity.CorrelationId).SingleOrDefault();

    private void AddOutboxMessage(TransferCompletedDomainEvent domainEvent, Guid? correlationId)
    {
        var integrationEvent = new TransferCompletedIntegrationEvent(
            domainEvent.Id, domainEvent.TransferId.Value, domainEvent.OccurredOnUtc, correlationId);
        OutboxMessages.Add(new OutboxMessage(integrationEvent.MessageId, integrationEvent.TransferId,
            correlationId, TransferCompletedIntegrationEvent.EventType,
            JsonSerializer.Serialize(integrationEvent), domainEvent.OccurredOnUtc));
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
