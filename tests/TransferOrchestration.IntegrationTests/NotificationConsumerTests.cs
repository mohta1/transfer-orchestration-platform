using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Data.Common;
using TransferOrchestration.Notification.Application;
using TransferOrchestration.Notification.Contracts;
using TransferOrchestration.Notification.Infrastructure.Persistence;
using TransferOrchestration.TransferManagement.Contracts.IntegrationEvents;
using TransferOrchestration.TransferManagement.Infrastructure.Outbox;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;

namespace TransferOrchestration.IntegrationTests;

public sealed class NotificationConsumerTests : IAsyncLifetime
{
    private readonly string _connectionString = Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING")
        ?? throw new InvalidOperationException("TASK-10 PostgreSQL tests require TEST_DATABASE_CONNECTION_STRING.");
    private readonly DateTimeOffset _now = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP SCHEMA IF EXISTS notification CASCADE";
        await command.ExecuteNonQueryAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
        await CreateProviderLedgerAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task DuplicateDeliveryCallsProviderOnceAndPersistsOneMarker()
    {
        var provider = new DurableRecordingProvider(_connectionString);
        var integrationEvent = Event();
        await DispatchAsync(integrationEvent, provider);
        await DispatchAsync(integrationEvent, provider);

        Assert.Equal(1, provider.InvocationCount);
        Assert.Equal(1, await provider.EffectCountAsync());
        var marker = Assert.Single(await ReadMarkersAsync(integrationEvent.MessageId));
        Assert.Equal(TransferCompletedNotificationConsumer.ConsumerName, marker.ConsumerName);
        Assert.Equal(_now, marker.ProcessedAtUtc);
    }

    [Fact]
    public async Task SameMessageCanBeProcessedByDifferentStableConsumerNames()
    {
        var provider = new DurableRecordingProvider(_connectionString);
        var integrationEvent = Event();
        await DispatchAsync(integrationEvent, provider, "notification.consumer-a.v1");
        await DispatchAsync(integrationEvent, provider, "notification.consumer-b.v1");

        Assert.Equal(2, provider.InvocationCount);
        Assert.Equal(2, await provider.EffectCountAsync());
        Assert.Equal(
            new[]
            {
                $"notification.consumer-a.v1:{integrationEvent.MessageId:D}",
                $"notification.consumer-b.v1:{integrationEvent.MessageId:D}"
            },
            await provider.KeysAsync());
        Assert.Equal(2, (await ReadMarkersAsync(integrationEvent.MessageId)).Count);
    }

    [Fact]
    public async Task ProviderFailureRollsBackMarkerAndLaterDeliveryRetries()
    {
        var provider = new DurableRecordingProvider(_connectionString) { Failure = new IOException("provider unavailable") };
        var integrationEvent = Event();
        await Assert.ThrowsAsync<IOException>(() => DispatchAsync(integrationEvent, provider));
        Assert.Empty(await ReadMarkersAsync(integrationEvent.MessageId));

        provider.Failure = null;
        await DispatchAsync(integrationEvent, provider);
        Assert.Equal(2, provider.InvocationCount);
        Assert.Equal(1, await provider.EffectCountAsync());
        Assert.Single(await ReadMarkersAsync(integrationEvent.MessageId));
    }

    [Fact]
    public async Task ConcurrentDuplicateDeliveryHasOneEffectAndOneMarker()
    {
        var provider = new DurableRecordingProvider(_connectionString);
        var integrationEvent = Event();
        var first = DispatchAsync(integrationEvent, provider);
        var second = DispatchAsync(integrationEvent, provider);
        await Task.WhenAll(first, second);

        Assert.Equal(1, provider.InvocationCount);
        Assert.Equal(1, await provider.EffectCountAsync());
        Assert.Single(await ReadMarkersAsync(integrationEvent.MessageId));
    }

    [Fact]
    public async Task DatabaseRejectsDuplicateConsumerMessageKey()
    {
        var integrationEvent = Event();
        await using (var context = CreateContext())
        {
            context.ProcessedMessages.Add(new ProcessedMessage(
                integrationEvent.MessageId, TransferCompletedNotificationConsumer.ConsumerName, _now));
            await context.SaveChangesAsync();
        }

        await using var duplicate = CreateContext();
        duplicate.ProcessedMessages.Add(new ProcessedMessage(
            integrationEvent.MessageId, TransferCompletedNotificationConsumer.ConsumerName, _now));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
    }

    [Fact]
    public async Task CancellationPropagatesWithoutProcessedMarker()
    {
        var integrationEvent = Event();
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DispatchAsync(integrationEvent, new DurableRecordingProvider(_connectionString), cancellationToken: source.Token));
        Assert.Empty(await ReadMarkersAsync(integrationEvent.MessageId));
    }

    [Fact]
    public async Task ProviderSuccessSurvivesLocalSaveFailureWithoutRepeatingEffect()
    {
        var provider = new DurableRecordingProvider(_connectionString);
        var integrationEvent = Event();
        await Assert.ThrowsAsync<InjectedFailureException>(() =>
            DispatchAsync(integrationEvent, provider, interceptors: [new FailOnceSaveInterceptor()]));
        Assert.Empty(await ReadMarkersAsync(integrationEvent.MessageId));

        await DispatchAsync(integrationEvent, provider);

        Assert.Equal(2, provider.InvocationCount);
        Assert.Equal(1, await provider.EffectCountAsync());
        Assert.Single(await ReadMarkersAsync(integrationEvent.MessageId));
    }

    [Fact]
    public async Task ProviderSuccessSurvivesLocalCommitFailureWithoutRepeatingEffect()
    {
        var provider = new DurableRecordingProvider(_connectionString);
        var integrationEvent = Event();
        await Assert.ThrowsAsync<InjectedFailureException>(() =>
            DispatchAsync(integrationEvent, provider, interceptors: [new FailOnceCommitInterceptor()]));
        Assert.Empty(await ReadMarkersAsync(integrationEvent.MessageId));

        await DispatchAsync(integrationEvent, provider);

        Assert.Equal(2, provider.InvocationCount);
        Assert.Equal(1, await provider.EffectCountAsync());
        Assert.Single(await ReadMarkersAsync(integrationEvent.MessageId));
    }

    [Fact]
    public async Task ProviderSuccessSurvivesPostProviderCancellationWithoutRepeatingEffect()
    {
        using var source = new CancellationTokenSource();
        var provider = new DurableRecordingProvider(_connectionString) { AfterEffect = source.Cancel };
        var integrationEvent = Event();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DispatchAsync(integrationEvent, provider, cancellationToken: source.Token));
        Assert.Empty(await ReadMarkersAsync(integrationEvent.MessageId));

        provider.AfterEffect = null;
        await DispatchAsync(integrationEvent, provider);

        Assert.Equal(2, provider.InvocationCount);
        Assert.Equal(1, await provider.EffectCountAsync());
        Assert.Single(await ReadMarkersAsync(integrationEvent.MessageId));
    }

    [Fact]
    public async Task MigrationCreatesCompositePrimaryKeyAndUtcTimestampColumn()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.table_constraints constraint_info
            JOIN information_schema.key_column_usage key_info
              ON constraint_info.constraint_name = key_info.constraint_name
             AND constraint_info.constraint_schema = key_info.constraint_schema
            WHERE constraint_info.constraint_schema = 'notification'
              AND constraint_info.table_name = 'processed_messages'
              AND constraint_info.constraint_type = 'PRIMARY KEY'
              AND key_info.column_name IN ('message_id', 'consumer_name')
            """;
        Assert.Equal(2L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task OutboxDispatchPathReachesIdempotentNotificationConsumer()
    {
        await DropTransferManagementSchemaAsync();
        await using var transferContext = CreateTransferContext();
        await transferContext.Database.MigrateAsync();
        var integrationEvent = Event();
        transferContext.OutboxMessages.Add(new OutboxMessage(integrationEvent.MessageId, integrationEvent.TransferId,
            integrationEvent.CorrelationId, TransferCompletedIntegrationEvent.EventType,
            System.Text.Json.JsonSerializer.Serialize(integrationEvent), integrationEvent.CompletedAtUtc));
        await transferContext.SaveChangesAsync();
        var provider = new DurableRecordingProvider(_connectionString);
        await using var notificationContext = CreateContext();
        var consumer = new TransferCompletedNotificationConsumer(
            notificationContext, provider, new FixedTimeProvider(_now));
        var options = Options.Create(new OutboxOptions
        {
            BatchSize = 1,
            LeaseDurationSeconds = 30,
            MaxAttempts = 3,
            InitialRetryDelaySeconds = 1,
            MaxRetryDelaySeconds = 10
        });
        var dispatcher = new OutboxBatchDispatcher(new OutboxStore(transferContext), consumer, options,
            NullLogger<OutboxBatchDispatcher>.Instance);

        Assert.Equal(1, await dispatcher.DispatchBatchAsync("notification-test", default));
        Assert.Equal(1, provider.InvocationCount);
        Assert.Equal(1, await provider.EffectCountAsync());
        Assert.Equal(
            $"{TransferCompletedNotificationConsumer.ConsumerName}:{integrationEvent.MessageId:D}",
            Assert.Single(await provider.KeysAsync()));
        Assert.Single(await ReadMarkersAsync(integrationEvent.MessageId));
        await using var verificationContext = CreateTransferContext();
        Assert.Equal(OutboxStatus.Published, (await verificationContext.OutboxMessages
            .SingleAsync(message => message.MessageId == integrationEvent.MessageId)).Status);
    }

    private async Task DispatchAsync(
        TransferCompletedIntegrationEvent integrationEvent,
        INotificationProvider provider,
        string? consumerName = null,
        IEnumerable<IInterceptor>? interceptors = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = CreateContext(interceptors);
        var consumer = consumerName is null
            ? new TransferCompletedNotificationConsumer(context, provider, new FixedTimeProvider(_now))
            : new TransferCompletedNotificationConsumer(context, provider, new FixedTimeProvider(_now), consumerName);
        await consumer.DispatchAsync(integrationEvent, cancellationToken);
    }

    private async Task<List<ProcessedMessage>> ReadMarkersAsync(Guid messageId)
    {
        await using var context = CreateContext();
        return await context.ProcessedMessages.AsNoTracking()
            .Where(message => message.MessageId == messageId).ToListAsync();
    }

    private TransferCompletedIntegrationEvent Event() =>
        new(Guid.NewGuid(), Guid.NewGuid(), _now, Guid.NewGuid());

    private NotificationDbContext CreateContext(IEnumerable<IInterceptor>? interceptors = null)
    {
        var builder = new DbContextOptionsBuilder<NotificationDbContext>().UseNpgsql(_connectionString,
            options => options.MigrationsHistoryTable("__EFMigrationsHistory", NotificationDbContext.Schema));
        if (interceptors is not null)
            builder.AddInterceptors(interceptors);
        return new NotificationDbContext(builder.Options);
    }

    private TransferManagementDbContext CreateTransferContext() => new(
        new DbContextOptionsBuilder<TransferManagementDbContext>().UseNpgsql(_connectionString,
            options => options.MigrationsHistoryTable("__EFMigrationsHistory", TransferManagementDbContext.Schema)).Options);

    private async Task DropTransferManagementSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP SCHEMA IF EXISTS transfer_management CASCADE";
        await command.ExecuteNonQueryAsync();
    }

    private async Task CreateProviderLedgerAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE SCHEMA IF NOT EXISTS notification_provider_test;
            DROP TABLE IF EXISTS notification_provider_test.effects;
            CREATE TABLE notification_provider_test.effects (
                idempotency_key text PRIMARY KEY,
                message_id uuid NOT NULL);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class DurableRecordingProvider(string connectionString) : INotificationProvider
    {
        private int _invocationCount;
        public int InvocationCount => Volatile.Read(ref _invocationCount);
        public Exception? Failure { get; set; }
        public Action? AfterEffect { get; set; }

        public async Task NotifyTransferCompletedAsync(
            NotificationIdempotencyKey idempotencyKey,
            TransferCompletedIntegrationEvent integrationEvent,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocationCount);
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null) throw Failure;
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO notification_provider_test.effects (idempotency_key, message_id)
                VALUES (@key, @message_id)
                ON CONFLICT (idempotency_key) DO NOTHING
                """;
            command.Parameters.AddWithValue("key", idempotencyKey.ToString());
            command.Parameters.AddWithValue("message_id", integrationEvent.MessageId);
            await command.ExecuteNonQueryAsync(cancellationToken);
            AfterEffect?.Invoke();
        }

        public async Task<long> EffectCountAsync() => await ScalarAsync<long>(
            "SELECT COUNT(*) FROM notification_provider_test.effects");

        public async Task<string[]> KeysAsync()
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT idempotency_key FROM notification_provider_test.effects ORDER BY idempotency_key";
            await using var reader = await command.ExecuteReaderAsync();
            var keys = new List<string>();
            while (await reader.ReadAsync()) keys.Add(reader.GetString(0));
            return keys.ToArray();
        }

        private async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return (T)(await command.ExecuteScalarAsync())!;
        }
    }

    private sealed class FailOnceSaveInterceptor : SaveChangesInterceptor
    {
        private int _failed;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Interlocked.Exchange(ref _failed, 1) == 0
                ? ValueTask.FromException<InterceptionResult<int>>(new InjectedFailureException())
                : ValueTask.FromResult(result);
    }

    private sealed class FailOnceCommitInterceptor : DbTransactionInterceptor
    {
        private int _failed;
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            Interlocked.Exchange(ref _failed, 1) == 0
                ? ValueTask.FromException<InterceptionResult>(new InjectedFailureException())
                : ValueTask.FromResult(result);
    }

    private sealed class InjectedFailureException : Exception;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
