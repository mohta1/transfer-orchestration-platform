using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using TransferOrchestration.TransferManagement.Contracts.IntegrationEvents;
using TransferOrchestration.TransferManagement.Domain.Transfers;
using TransferOrchestration.TransferManagement.Domain.Transfers.Events;
using TransferOrchestration.TransferManagement.Infrastructure.Outbox;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;

namespace TransferOrchestration.IntegrationTests;

public sealed class TransactionalOutboxTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260811190000_AddNetworkSubmissionReference";
    private readonly string _connectionString = Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING")
        ?? throw new InvalidOperationException("TASK-09 PostgreSQL tests require TEST_DATABASE_CONNECTION_STRING.");
    private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 8, 11, 20, 0, 0, TimeSpan.Zero));

    public async Task InitializeAsync()
    {
        await DropSchemaAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CompletedTransferAndOutboxCommitAtomically()
    {
        var (transfer, domainEvent) = await CompleteAsync(TransferType.DomesticInterbank);
        await using var context = CreateContext();
        var persisted = await context.Transfers.SingleAsync(item => item.Id == transfer.Id);
        var message = await context.OutboxMessages.SingleAsync(item => item.TransferId == transfer.Id.Value);
        var payload = System.Text.Json.JsonSerializer.Deserialize<TransferCompletedIntegrationEvent>(message.Payload);
        Assert.Equal(TransferState.Completed, persisted.State);
        Assert.Equal(domainEvent.Id, message.MessageId);
        Assert.Equal(TransferCompletedIntegrationEvent.EventType, message.Type);
        Assert.Equal(transfer.Id.Value, payload?.TransferId);
        Assert.Equal(domainEvent.OccurredOnUtc, payload?.CompletedAtUtc);
    }

    [Fact]
    public async Task InternalCompletionCreatesTheSameSingleContract()
    {
        var (transfer, _) = await CompleteAsync(TransferType.InternalBank);
        await using var context = CreateContext();
        var message = await context.OutboxMessages.SingleAsync(item => item.TransferId == transfer.Id.Value);
        Assert.Equal(TransferCompletedIntegrationEvent.EventType, message.Type);
    }

    [Fact]
    public async Task FailedSaveCommitsNeitherCompletionNorOutboxAndPreservesMessageIdForRetry()
    {
        var transfer = CreateReadyTransfer(TransferType.InternalBank);
        await using var context = CreateContext();
        context.Transfers.Add(transfer);
        await context.SaveChangesAsync();
        transfer.CompleteInternalTransfer(_clock.GetUtcNow());
        var original = Assert.IsType<TransferCompletedDomainEvent>(Assert.Single(transfer.DomainEvents));
        context.OutboxMessages.Add(new OutboxMessage(Guid.NewGuid(), transfer.Id.Value, null,
            new string('x', 101), "{}", _clock.GetUtcNow()));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(original.Id, Assert.IsType<TransferCompletedDomainEvent>(Assert.Single(transfer.DomainEvents)).Id);
        await using (var rolledBack = CreateContext())
        {
            Assert.Equal(TransferState.BalanceReserved, (await rolledBack.Transfers.AsNoTracking()
                .SingleAsync(item => item.Id == transfer.Id)).State);
            Assert.Empty(await rolledBack.OutboxMessages.AsNoTracking()
                .Where(item => item.TransferId == transfer.Id.Value || item.MessageId == original.Id).ToListAsync());
        }
        context.ChangeTracker.Entries<OutboxMessage>().Single(entry => entry.Entity.Type.Length == 101).State = EntityState.Detached;
        await context.SaveChangesAsync();

        await using var fresh = CreateContext();
        Assert.Equal(TransferState.Completed, (await fresh.Transfers.SingleAsync(item => item.Id == transfer.Id)).State);
        Assert.Equal(original.Id, (await fresh.OutboxMessages.SingleAsync(item => item.TransferId == transfer.Id.Value)).MessageId);
    }

    [Fact]
    public async Task SameContextRetryDoesNotTrackDuplicateMessageId()
    {
        var transfer = CreateReadyTransfer(TransferType.InternalBank);
        await using var context = CreateContext();
        context.Transfers.Add(transfer);
        await context.SaveChangesAsync();
        transfer.CompleteInternalTransfer(_clock.GetUtcNow());
        context.OutboxMessages.Add(new OutboxMessage(Guid.NewGuid(), transfer.Id.Value, null,
            new string('x', 101), "{}", _clock.GetUtcNow()));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(2, context.ChangeTracker.Entries<OutboxMessage>().Count());
        context.ChangeTracker.Entries<OutboxMessage>().Single(entry => entry.Entity.Type.Length == 101).State = EntityState.Detached;
        await context.SaveChangesAsync();
        Assert.Single(await context.OutboxMessages.Where(item => item.TransferId == transfer.Id.Value).ToListAsync());
    }

    [Fact]
    public async Task SuccessfulDispatchMarksPublished()
    {
        var (_, domainEvent) = await CompleteAsync(TransferType.InternalBank);
        var dispatcher = new RecordingDispatcher();
        Assert.Equal(1, await DispatchAsync("one", dispatcher));
        var message = await ReadMessageAsync(domainEvent.Id);
        Assert.Equal(OutboxStatus.Published, message.Status);
        Assert.NotNull(message.PublishedAtUtc);
    }

    [Fact]
    public async Task DispatchFailurePersistsRetryMetadata()
    {
        var (_, domainEvent) = await CompleteAsync(TransferType.InternalBank);
        await DispatchAsync("one", new RecordingDispatcher { Failure = new IOException("temporary") });
        var message = await ReadMessageAsync(domainEvent.Id);
        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Equal(1, message.Attempts);
        Assert.True(message.NextAttemptAtUtc > _clock.GetUtcNow());
        Assert.Equal("temporary", message.LastError);
        Assert.Null(message.LockedBy);
    }

    [Fact]
    public async Task DurableNextAttemptBecomesEligibleWithoutSleeping()
    {
        var (_, domainEvent) = await CompleteAsync(TransferType.InternalBank);
        await DispatchAsync("one", new RecordingDispatcher { Failure = new IOException("temporary") });
        Assert.Equal(0, await DispatchAsync("two", new RecordingDispatcher()));
        await MakeRetriesDueAsync();
        var retry = new RecordingDispatcher();
        Assert.Equal(1, await DispatchAsync("two", retry));
        Assert.Equal(domainEvent.Id, Assert.Single(retry.Deliveries).MessageId);
    }

    [Fact]
    public async Task NewContextRediscoversPendingWork()
    {
        var (_, domainEvent) = await CompleteAsync(TransferType.InternalBank);
        var restartedDispatcher = new RecordingDispatcher();
        await DispatchAsync("restarted", restartedDispatcher);
        Assert.Equal(domainEvent.Id, Assert.Single(restartedDispatcher.Deliveries).MessageId);
    }

    [Fact]
    public async Task ExpiredClaimIsReclaimable()
    {
        await CompleteAsync(TransferType.InternalBank);
        await using (var context = CreateContext())
            Assert.NotNull(await new OutboxStore(context).ClaimOneAsync("crashed", TimeSpan.FromSeconds(30), default));
        await ExpireAllLeasesAsync();
        await using var reclaimContext = CreateContext();
        Assert.NotNull(await new OutboxStore(reclaimContext).ClaimOneAsync("replacement", TimeSpan.FromSeconds(30), default));
    }

    [Fact]
    public async Task RenewBeforeDispatchReturnsPostgreSqlTimestampWithTimeZoneLeaseToken()
    {
        await CompleteAsync(TransferType.InternalBank);
        await using var context = CreateContext();
        var store = new OutboxStore(context);
        var original = Assert.IsType<OutboxClaim>(
            await store.ClaimOneAsync("worker", TimeSpan.FromSeconds(30), default));

        var renewed = Assert.IsType<OutboxClaim>(
            await store.RenewBeforeDispatchAsync(original, "worker", TimeSpan.FromMinutes(2), default));

        Assert.True(renewed.LockedUntilUtc > original.LockedUntilUtc);
        await using var fresh = CreateContext();
        var persisted = await fresh.OutboxMessages.AsNoTracking()
            .SingleAsync(item => item.MessageId == original.MessageId);
        Assert.Equal(renewed.LockedUntilUtc, persisted.LockedUntilUtc);
        Assert.Equal(TimeSpan.Zero, renewed.LockedUntilUtc.Offset);
    }

    [Fact]
    public async Task RepeatedRenewalLossConsumesClaimBudgetAndReturnsWithoutDispatching()
    {
        const int batchSize = 3;
        var store = new RenewalLosingStore();
        var dispatcher = new RecordingDispatcher();
        var batch = new OutboxBatchDispatcher(store, dispatcher, Options.Create(TestOptions(batchSize: batchSize)),
            NullLogger<OutboxBatchDispatcher>.Instance);

        var processed = await batch.DispatchBatchAsync("worker", default);

        Assert.Equal(0, processed);
        Assert.Equal(batchSize, store.ClaimCount);
        Assert.Equal(batchSize, store.RenewCount);
        Assert.Equal(0, store.MarkPublishedCount);
        Assert.Equal(0, store.MarkFailureCount);
        Assert.Empty(dispatcher.Deliveries);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            batch.DispatchBatchAsync("worker", new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task ConcurrentWorkersCannotOwnTheSameLease()
    {
        await CompleteAsync(TransferType.InternalBank);
        var claims = await Task.WhenAll(ClaimAsync("a", 1), ClaimAsync("b", 1));
        Assert.Equal(1, claims.Sum(items => items.Count));
    }

    [Fact]
    public async Task ConcurrentWorkersCanClaimDifferentMessages()
    {
        await CompleteAsync(TransferType.InternalBank);
        await CompleteAsync(TransferType.InternalBank);
        var claims = await Task.WhenAll(ClaimAsync("a", 1), ClaimAsync("b", 1));
        Assert.Equal(2, claims.Sum(items => items.Count));
        Assert.Equal(2, claims.SelectMany(items => items).Select(item => item.MessageId).Distinct().Count());
    }

    [Fact]
    public async Task StaleOwnerCannotOverwriteReclaimedLease()
    {
        await CompleteAsync(TransferType.InternalBank);
        var stale = Assert.Single(await ClaimAsync("a", 1));
        await ExpireAllLeasesAsync();
        var current = Assert.Single(await ClaimAsync("b", 1));
        await using var context = CreateContext();
        Assert.Equal(0, await new OutboxStore(context).MarkPublishedAsync(stale, "a", default));
        Assert.Equal(1, await new OutboxStore(context).MarkPublishedAsync(current, "b", default));
    }

    [Fact]
    public async Task CrashAfterDeliveryPermitsSameMessageIdToBeDeliveredAgain()
    {
        var (_, domainEvent) = await CompleteAsync(TransferType.InternalBank);
        var first = Assert.Single(await ClaimAsync("crashed", 1));
        var receiver = new RecordingDispatcher();
        await receiver.DispatchAsync(System.Text.Json.JsonSerializer.Deserialize<TransferCompletedIntegrationEvent>(first.Payload)!, default);
        await ExpireAllLeasesAsync();
        await DispatchAsync("restart", receiver);
        Assert.Equal(new[] { domainEvent.Id, domainEvent.Id }, receiver.Deliveries.Select(item => item.MessageId));
    }

    [Fact]
    public async Task PoisonMessageStopsAtConfiguredMaxAttempts()
    {
        var (_, domainEvent) = await CompleteAsync(TransferType.InternalBank);
        var options = TestOptions(maxAttempts: 2);
        await DispatchAsync("one", new RecordingDispatcher { Failure = new IOException("poison") }, options);
        await MakeRetriesDueAsync();
        await DispatchAsync("two", new RecordingDispatcher { Failure = new IOException("poison") }, options);
        Assert.Equal(OutboxStatus.DeadLetter, (await ReadMessageAsync(domainEvent.Id)).Status);
        _clock.Advance(TimeSpan.FromDays(1));
        Assert.Empty(await ClaimAsync("three", 1));
    }

    [Fact]
    public async Task UnsupportedTypeAndMalformedJsonBecomeDeadLetter()
    {
        await InsertRawMessageAsync("unsupported", "{}");
        await InsertRawMessageAsync(TransferCompletedIntegrationEvent.EventType, "{}");
        Assert.Equal(2, await DispatchAsync("one", new RecordingDispatcher()));
        await using var context = CreateContext();
        Assert.All(await context.OutboxMessages.ToListAsync(), item => Assert.Equal(OutboxStatus.DeadLetter, item.Status));
    }

    [Fact]
    public async Task PublishedAndDeadLetterAreNeverClaimedAgain()
    {
        await CompleteAsync(TransferType.InternalBank);
        await CompleteAsync(TransferType.InternalBank);
        await DispatchAsync("one", new RecordingDispatcher(), TestOptions(batchSize: 1));
        await DispatchAsync("two", new RecordingDispatcher { Failure = new IOException("poison") }, TestOptions(batchSize: 1, maxAttempts: 1));
        _clock.Advance(TimeSpan.FromDays(1));
        Assert.Empty(await ClaimAsync("three", 10));
    }

    [Fact]
    public async Task EmptyOutboxAllowsSafeDowngrade()
    {
        await using var context = CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        Assert.False(await TableExistsAsync());
    }

    [Fact]
    public async Task ForwardMigrationBackfillsReachableHistoricalCompletedTransfer()
    {
        await DropSchemaAsync();
        await using (var oldContext = CreateContext())
            await oldContext.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var transferId = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO transfer_management.transfers
                    (id, source_account_id, destination_account_id, amount, currency, type, state, version, created_at_utc, updated_at_utc)
                VALUES (@id, @source, @destination, 10, 'GBP', 'InternalBank', 'Completed', 7, @created, @completed)
                """;
            command.Parameters.AddWithValue("id", transferId);
            command.Parameters.AddWithValue("source", Guid.NewGuid());
            command.Parameters.AddWithValue("destination", Guid.NewGuid());
            command.Parameters.AddWithValue("created", _clock.GetUtcNow().AddMinutes(-1));
            command.Parameters.AddWithValue("completed", _clock.GetUtcNow());
            await command.ExecuteNonQueryAsync();
        }

        await using var upgraded = CreateContext();
        await upgraded.Database.MigrateAsync();
        var message = await upgraded.OutboxMessages.SingleAsync(item => item.TransferId == transferId);
        Assert.Equal(TransferCompletedIntegrationEvent.EventType, message.Type);
        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Equal(_clock.GetUtcNow(), message.OccurredAtUtc);
    }

    [Fact]
    public async Task NonEmptyOutboxRefusesDowngradeAndRollsBackHistory()
    {
        await CompleteAsync(TransferType.InternalBank);
        await using var context = CreateContext();
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => context.GetService<IMigrator>().MigrateAsync(PreviousMigration));
        Assert.Contains("Cannot downgrade TASK-09", exception.ToString(), StringComparison.Ordinal);
        Assert.True(await TableExistsAsync());
        Assert.Single(await context.OutboxMessages.AsNoTracking().ToListAsync());
        Assert.Contains("20260811231112_AddTransactionalOutbox", await context.Database.GetAppliedMigrationsAsync());
    }

    private async Task<(Transfer Transfer, TransferCompletedDomainEvent Event)> CompleteAsync(TransferType type)
    {
        var transfer = CreateReadyTransfer(type);
        await using var context = CreateContext();
        context.Transfers.Add(transfer);
        await context.SaveChangesAsync();
        if (type == TransferType.InternalBank) transfer.CompleteInternalTransfer(_clock.GetUtcNow());
        else { transfer.BeginExternalSubmission(_clock.GetUtcNow()); transfer.MarkSettlementPending(_clock.GetUtcNow()); transfer.CompleteSettlement(_clock.GetUtcNow()); }
        var domainEvent = Assert.IsType<TransferCompletedDomainEvent>(Assert.Single(transfer.DomainEvents));
        await context.SaveChangesAsync();
        return (transfer, domainEvent);
    }

    private Transfer CreateReadyTransfer(TransferType type)
    {
        var transfer = Transfer.Create(Guid.NewGuid(), Guid.NewGuid(), 10m, "GBP", type, _clock.GetUtcNow());
        transfer.Submit(_clock.GetUtcNow()); transfer.RequestAuthorisation(_clock.GetUtcNow()); transfer.Authorise(_clock.GetUtcNow());
        transfer.BeginFraudScreening(_clock.GetUtcNow()); transfer.RequestBalanceReservation(_clock.GetUtcNow()); transfer.MarkBalanceReserved(_clock.GetUtcNow());
        return transfer;
    }

    private async Task<int> DispatchAsync(string worker, RecordingDispatcher dispatcher, OutboxOptions? options = null)
    {
        await using var context = CreateContext();
        var batch = new OutboxBatchDispatcher(new OutboxStore(context), dispatcher, Options.Create(options ?? TestOptions()),
            NullLogger<OutboxBatchDispatcher>.Instance);
        return await batch.DispatchBatchAsync(worker, default);
    }

    private async Task<IReadOnlyList<OutboxClaim>> ClaimAsync(string worker, int batchSize)
    {
        await using var context = CreateContext();
        var claims = new List<OutboxClaim>();
        for (var i = 0; i < batchSize; i++)
        {
            var claim = await new OutboxStore(context).ClaimOneAsync(worker, TimeSpan.FromSeconds(30), default);
            if (claim is null) break;
            claims.Add(claim);
        }
        return claims;
    }

    private Task ExpireAllLeasesAsync() => ExecuteSqlAsync(
        "UPDATE transfer_management.outbox_messages SET \"LockedUntilUtc\" = CURRENT_TIMESTAMP - interval '1 second' WHERE \"LockedUntilUtc\" IS NOT NULL");

    private Task MakeRetriesDueAsync() => ExecuteSqlAsync(
        "UPDATE transfer_management.outbox_messages SET \"NextAttemptAtUtc\" = CURRENT_TIMESTAMP - interval '1 second' WHERE \"Status\" = 0");

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<OutboxMessage> ReadMessageAsync(Guid id)
    {
        await using var context = CreateContext();
        return await context.OutboxMessages.AsNoTracking().SingleAsync(item => item.MessageId == id);
    }

    private async Task InsertRawMessageAsync(string type, string payload)
    {
        await using var connection = new NpgsqlConnection(_connectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO transfer_management.outbox_messages (\"MessageId\", \"TransferId\", \"Type\", \"Payload\", \"OccurredAtUtc\", \"Status\", \"Attempts\", \"NextAttemptAtUtc\") VALUES (@id, @transfer, @type, CAST(@payload AS jsonb), @now, 0, 0, @now)";
        command.Parameters.AddWithValue("id", Guid.NewGuid()); command.Parameters.AddWithValue("transfer", Guid.NewGuid());
        command.Parameters.AddWithValue("type", type); command.Parameters.AddWithValue("payload", payload); command.Parameters.AddWithValue("now", _clock.GetUtcNow());
        await command.ExecuteNonQueryAsync();
    }

    private static OutboxOptions TestOptions(int batchSize = 20, int maxAttempts = 8) => new()
    { BatchSize = batchSize, MaxAttempts = maxAttempts, InitialRetryDelaySeconds = 1, MaxRetryDelaySeconds = 10, LeaseDurationSeconds = 30 };

    private TransferManagementDbContext CreateContext() => new(new DbContextOptionsBuilder<TransferManagementDbContext>()
        .UseNpgsql(_connectionString, options => options.MigrationsHistoryTable("__EFMigrationsHistory", TransferManagementDbContext.Schema)).Options);

    private async Task DropSchemaAsync() { await using var connection = new NpgsqlConnection(_connectionString); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = "DROP SCHEMA IF EXISTS transfer_management CASCADE"; await command.ExecuteNonQueryAsync(); }
    private async Task<bool> TableExistsAsync() { await using var connection = new NpgsqlConnection(_connectionString); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = "SELECT to_regclass('transfer_management.outbox_messages') IS NOT NULL"; return (bool)(await command.ExecuteScalarAsync())!; }

    private sealed class RecordingDispatcher : IIntegrationEventDispatcher
    {
        public Exception? Failure { get; init; }
        public List<TransferCompletedIntegrationEvent> Deliveries { get; } = [];
        public Task DispatchAsync(TransferCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Deliveries.Add(integrationEvent); return Failure is null ? Task.CompletedTask : Task.FromException(Failure); }
    }

    private sealed class RenewalLosingStore : IOutboxStore
    {
        private readonly OutboxClaim _claim = new(1, Guid.NewGuid(), Guid.NewGuid(), null,
            TransferCompletedIntegrationEvent.EventType, "{}", 0, DateTimeOffset.UtcNow);

        public int ClaimCount { get; private set; }
        public int RenewCount { get; private set; }
        public int MarkPublishedCount { get; private set; }
        public int MarkFailureCount { get; private set; }

        public Task<OutboxClaim?> ClaimOneAsync(string workerId, TimeSpan leaseDuration, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ClaimCount++;
            return Task.FromResult<OutboxClaim?>(_claim);
        }

        public Task<OutboxClaim?> RenewBeforeDispatchAsync(
            OutboxClaim claim, string workerId, TimeSpan leaseDuration, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            RenewCount++;
            return Task.FromResult<OutboxClaim?>(null);
        }

        public Task<int> MarkPublishedAsync(OutboxClaim claim, string workerId, CancellationToken token)
        {
            MarkPublishedCount++;
            return Task.FromResult(1);
        }

        public Task<int> MarkFailureAsync(OutboxClaim claim, string workerId, TimeSpan retryDelay, string error,
            bool deadLetter, CancellationToken token)
        {
            MarkFailureCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
        public void Set(DateTimeOffset value) => _now = value;
    }
}
