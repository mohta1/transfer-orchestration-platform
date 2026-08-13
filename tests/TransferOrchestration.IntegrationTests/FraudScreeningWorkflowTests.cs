using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using TransferOrchestration.AccountBalance;
using TransferOrchestration.AccountBalance.Domain.Accounts;
using TransferOrchestration.AccountBalance.Infrastructure.Persistence;
using TransferOrchestration.TransferManagement;
using TransferOrchestration.TransferManagement.Application.FraudScreening;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Domain.Transfers;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;

namespace TransferOrchestration.IntegrationTests;

[Collection("PostgreSQL fraud screening")]
public sealed class FraudScreeningWorkflowTests : IAsyncLifetime
{
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING")
        ?? throw new InvalidOperationException(
            "Destructive PostgreSQL tests require TEST_DATABASE_CONNECTION_STRING.");

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP SCHEMA IF EXISTS transfer_management CASCADE; DROP SCHEMA IF EXISTS account_balance CASCADE;";
        await command.ExecuteNonQueryAsync();

        await using var provider = CreateProvider(_connectionString, new FixedFraudScreening(FraudScreeningResult.Approved));
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AccountBalanceDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>().Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ApprovedFraudSchedulesExactlyOneReserveBalanceAction()
    {
        var transferId = await SeedPendingFraudScreeningAsync();
        await using var provider = CreateProvider(_connectionString, new FixedFraudScreening(FraudScreeningResult.Approved));

        Assert.Equal(1, await DispatchFraudAsync(provider));

        var process = await ProcessSnapshotAsync(transferId);
        Assert.Equal(TransferState.PendingBalanceReservation, (await TransferStateAsync(transferId)));
        Assert.Equal(TransferProcessAction.ReserveBalance, process.NextAction);
        Assert.Equal(TransferProcessStatus.Active, process.Status);
    }

    [Fact]
    public async Task RejectedFraudCreatesNoReservationAndCannotProgress()
    {
        var accountId = await SeedAccountAsync(1_000m);
        var transferId = await SeedPendingFraudScreeningAsync(accountId);
        await using var provider = CreateProvider(_connectionString, new FixedFraudScreening(FraudScreeningResult.Rejected));

        Assert.Equal(1, await DispatchFraudAsync(provider));

        Assert.Equal(TransferState.FraudRejected, await TransferStateAsync(transferId));
        var process = await ProcessSnapshotAsync(transferId);
        Assert.Equal(TransferProcessStatus.Completed, process.Status);
        Assert.Equal(0, await ReservationCountAsync(transferId.Value));
    }

    [Fact]
    public async Task ManualReviewResultEscalatesWithNoReservation()
    {
        var accountId = await SeedAccountAsync(1_000m);
        var transferId = await SeedPendingFraudScreeningAsync(accountId);
        await using var provider = CreateProvider(_connectionString, new FixedFraudScreening(FraudScreeningResult.ManualReviewRequired));

        Assert.Equal(1, await DispatchFraudAsync(provider));

        Assert.Equal(TransferState.ManualReviewRequired, await TransferStateAsync(transferId));
        Assert.Equal(TransferProcessStatus.Completed, (await ProcessSnapshotAsync(transferId)).Status);
        Assert.Equal(0, await ReservationCountAsync(transferId.Value));
    }

    [Fact]
    public async Task TimeoutLeavesDurableRecoverableWork()
    {
        var transferId = await SeedPendingFraudScreeningAsync();
        await using var provider = CreateProvider(_connectionString, new FixedFraudScreening(FraudScreeningResult.Timeout));

        Assert.Equal(FraudScreeningStepOutcome.RetryScheduled, await ExecuteFraudStepAsync(provider, transferId));

        var process = await ProcessSnapshotAsync(transferId);
        Assert.Equal(TransferState.PendingFraudScreening, await TransferStateAsync(transferId));
        Assert.Equal(TransferProcessAction.RequestFraudScreening, process.NextAction);
        Assert.Equal(1, process.AttemptCount);
        Assert.NotNull(process.NextAttemptAtUtc);
    }

    [Fact]
    public async Task TemporarilyUnavailableLeavesDurableRecoverableWork()
    {
        var transferId = await SeedPendingFraudScreeningAsync();
        await using var provider = CreateProvider(_connectionString, new FixedFraudScreening(FraudScreeningResult.TemporarilyUnavailable));

        Assert.Equal(FraudScreeningStepOutcome.RetryScheduled, await ExecuteFraudStepAsync(provider, transferId));

        var process = await ProcessSnapshotAsync(transferId);
        Assert.Equal(TransferProcessAction.RequestFraudScreening, process.NextAction);
        Assert.Equal(1, process.AttemptCount);
    }

    [Fact]
    public async Task RetryUsesTheSameStableScreeningIdentity()
    {
        var transferId = await SeedPendingFraudScreeningAsync();
        var fraud = new SequencedFraudScreening(FraudScreeningResult.Timeout, FraudScreeningResult.Approved);
        await using var provider = CreateProvider(_connectionString, fraud);

        Assert.Equal(FraudScreeningStepOutcome.RetryScheduled, await ExecuteFraudStepAsync(provider, transferId));
        var process = await ProcessSnapshotAsync(transferId);
        await AdvanceProcessDueAsync(_connectionString, transferId, process);

        Assert.Equal(FraudScreeningStepOutcome.Approved, await ExecuteFraudStepAsync(provider, transferId));
        Assert.Equal(2, fraud.Invocations);
        Assert.All(fraud.ObservedTransferIds, id => Assert.Equal(transferId.Value, id));
    }

    [Fact]
    public async Task RetryCountAndNextAttemptTimestampPersistCorrectly()
    {
        var transferId = await SeedPendingFraudScreeningAsync();
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var fraud = new FixedFraudScreening(FraudScreeningResult.Timeout);
        await using var provider = CreateProvider(_connectionString, fraud, clock);

        await ExecuteFraudStepAsync(provider, transferId);
        var first = await ProcessSnapshotAsync(transferId);
        Assert.Equal(1, first.AttemptCount);
        AssertRetryDelay(clock.GetUtcNow(), first.NextAttemptAtUtc, expectedSeconds: 2);

        clock.Advance(TimeSpan.FromSeconds(2));
        await DispatchFraudAsync(provider);
        var second = await ProcessSnapshotAsync(transferId);
        Assert.Equal(2, second.AttemptCount);
        AssertRetryDelay(clock.GetUtcNow(), second.NextAttemptAtUtc, expectedSeconds: 4);
    }

    [Fact]
    public async Task MaximumAttemptsEscalateToManualReview()
    {
        var transferId = await SeedPendingFraudScreeningAsync();
        await using var provider = CreateProvider(_connectionString, new FixedFraudScreening(FraudScreeningResult.Timeout));
        await SetProcessAttemptCountAsync(transferId, 2);

        Assert.Equal(FraudScreeningStepOutcome.EscalatedToManualReview, await ExecuteFraudStepAsync(provider, transferId));

        Assert.Equal(TransferState.ManualReviewRequired, await TransferStateAsync(transferId));
        Assert.Equal(TransferProcessStatus.Completed, (await ProcessSnapshotAsync(transferId)).Status);
    }

    [Fact]
    public async Task RestartRediscoversPendingFraudWork()
    {
        var transferId = await SeedPendingFraudScreeningAsync();
        var fraud = new FixedFraudScreening(FraudScreeningResult.Approved);

        await using var restartedProvider = CreateProvider(_connectionString, fraud);
        Assert.Equal(1, await DispatchFraudAsync(restartedProvider));
        Assert.Equal(TransferState.PendingBalanceReservation, await TransferStateAsync(transferId));
    }

    [Fact]
    public async Task ConcurrentDuplicateClaimsProduceOneFraudTransition()
    {
        var transferId = await SeedPendingFraudScreeningAsync();
        var gate = new BlockingFraudExecutionGate(FraudScreeningResult.Approved);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow.AddMinutes(1));
        await using var providerA = CreateProvider(_connectionString, gate, clock);
        await using var providerB = CreateProvider(_connectionString, gate, clock);

        var firstDispatch = DispatchFraudAsync(providerA);
        await gate.Entered;

        clock.Advance(FraudScreeningDueWorkDispatcherLease + TimeSpan.FromSeconds(1));
        Assert.Equal(1, await DispatchFraudAsync(providerB));

        gate.Release();
        Assert.Equal(1, await firstDispatch);
        Assert.Equal(TransferState.PendingBalanceReservation, await TransferStateAsync(transferId));
        var process = await ProcessSnapshotAsync(transferId);
        Assert.Equal(TransferProcessAction.ReserveBalance, process.NextAction);
        Assert.Equal(TransferProcessStatus.Active, process.Status);
        Assert.Equal(0, await ReservationCountAsync(transferId.Value));
    }

    [Fact]
    public async Task CorrelationSurvivesWorkerRetryAndRestartProcessing()
    {
        var correlationId = Guid.NewGuid();
        var transferId = await SeedPendingFraudScreeningAsync(correlationId: correlationId);
        var fraud = new SequencedFraudScreening(FraudScreeningResult.Timeout, FraudScreeningResult.Approved);
        await using (var firstProvider = CreateProvider(_connectionString, fraud))
        {
            await ExecuteFraudStepAsync(firstProvider, transferId);
        }

        var midProcess = await ProcessSnapshotAsync(transferId);
        Assert.Equal(correlationId, midProcess.CorrelationId);
        await AdvanceProcessDueAsync(_connectionString, transferId, midProcess);

        await using var restartedProvider = CreateProvider(_connectionString, fraud);
        await DispatchFraudAsync(restartedProvider);
        Assert.Equal(correlationId, (await ProcessSnapshotAsync(transferId)).CorrelationId);
    }

    private static readonly TimeSpan FraudScreeningDueWorkDispatcherLease = TimeSpan.FromSeconds(30);

    private static void AssertRetryDelay(
        DateTimeOffset nowUtc,
        DateTimeOffset? nextAttemptAtUtc,
        int expectedSeconds)
    {
        Assert.NotNull(nextAttemptAtUtc);
        var delay = nextAttemptAtUtc.Value - nowUtc;
        Assert.Equal(expectedSeconds, delay.TotalSeconds, precision: 3);
    }

    private async Task<TransferId> SeedPendingFraudScreeningAsync(
        Guid? sourceAccountId = null,
        Guid? correlationId = null)
    {
        var accountId = sourceAccountId ?? await SeedAccountAsync(1_000m);
        var now = DateTimeOffset.UtcNow;
        var transfer = Transfer.Create(accountId, Guid.NewGuid(), 100m, "GBP", TransferType.DomesticInterbank, now);
        transfer.Submit(now);
        transfer.RequestAuthorisation(now);
        transfer.Authorise(now);
        transfer.BeginFraudScreening(now);
        var process = TransferProcessState.Create(transfer.Id, correlationId ?? Guid.NewGuid(), now);
        process.Schedule(TransferProcessAction.RequestFraudScreening, now, now);

        await using var provider = CreateProvider(_connectionString, new FixedFraudScreening(FraudScreeningResult.Approved));
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>();
        context.Transfers.Add(transfer);
        context.TransferProcessStates.Add(process);
        await context.SaveChangesAsync();
        return transfer.Id;
    }

    private async Task<Guid> SeedAccountAsync(decimal balance) =>
        await SeedAccountCoreAsync(_connectionString, balance);

    private static async Task<Guid> SeedAccountCoreAsync(string connectionString, decimal balance)
    {
        var id = Guid.NewGuid();
        await using var provider = CreateProvider(connectionString, new FixedFraudScreening(FraudScreeningResult.Approved));
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AccountBalanceDbContext>();
        context.Accounts.Add(Account.Create(id, "GBP", balance, AccountStatus.Active));
        await context.SaveChangesAsync();
        return id;
    }

    private static async Task<int> DispatchFraudAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return await FraudScreeningTestSupport.DispatchDueFraudScreeningAsync(scope.ServiceProvider);
    }

    private static async Task<FraudScreeningStepOutcome> ExecuteFraudStepAsync(
        ServiceProvider provider,
        TransferId transferId)
    {
        await using var scope = provider.CreateAsyncScope();
        var process = await scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>()
            .TransferProcessStates.AsNoTracking()
            .SingleAsync(candidate => candidate.TransferId == transferId);
        return await scope.ServiceProvider.GetRequiredService<IFraudScreeningProcessStep>()
            .ExecuteAsync(transferId, process.Version, CancellationToken.None);
    }

    private static async Task AdvanceProcessDueAsync(string connectionString, TransferId transferId, ProcessSnapshot process)
    {
        await using var provider = CreateProvider(connectionString, new FixedFraudScreening(FraudScreeningResult.Approved));
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>();
        var entity = await context.TransferProcessStates.SingleAsync(candidate => candidate.TransferId == transferId);
        var now = DateTimeOffset.UtcNow;
        entity.RecordAttempt(now, now);
        await context.SaveChangesAsync();
    }

    private async Task SetProcessAttemptCountAsync(TransferId transferId, int count) =>
        await SetProcessAttemptCountCoreAsync(_connectionString, transferId, count);

    private static async Task SetProcessAttemptCountCoreAsync(string connectionString, TransferId transferId, int count)
    {
        await using var provider = CreateProvider(connectionString, new FixedFraudScreening(FraudScreeningResult.Approved));
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>();
        var process = await context.TransferProcessStates.SingleAsync(candidate => candidate.TransferId == transferId);
        var now = DateTimeOffset.UtcNow;
        for (var attempt = 0; attempt < count; attempt++)
        {
            process.RecordAttempt(now, now);
        }

        await context.SaveChangesAsync();
    }

    private Task<TransferState> TransferStateAsync(TransferId transferId) =>
        TransferStateCoreAsync(_connectionString, transferId);

    private static async Task<TransferState> TransferStateCoreAsync(string connectionString, TransferId transferId)
    {
        await using var provider = CreateProvider(connectionString, new FixedFraudScreening(FraudScreeningResult.Approved));
        await using var scope = provider.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>()
            .Transfers.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == transferId)).State;
    }

    private Task<ProcessSnapshot> ProcessSnapshotAsync(TransferId transferId) =>
        ProcessSnapshotCoreAsync(_connectionString, transferId);

    private static async Task<ProcessSnapshot> ProcessSnapshotCoreAsync(string connectionString, TransferId transferId)
    {
        await using var provider = CreateProvider(connectionString, new FixedFraudScreening(FraudScreeningResult.Approved));
        await using var scope = provider.CreateAsyncScope();
        var process = await scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>()
            .TransferProcessStates.AsNoTracking()
            .SingleAsync(candidate => candidate.TransferId == transferId);
        return new ProcessSnapshot(
            process.Status,
            process.NextAction,
            process.AttemptCount,
            process.NextAttemptAtUtc,
            process.CorrelationId);
    }

    private Task<int> ReservationCountAsync(Guid transferId) =>
        ReservationCountCoreAsync(_connectionString, transferId);

    private static async Task<int> ReservationCountCoreAsync(string connectionString, Guid transferId)
    {
        await using var provider = CreateProvider(connectionString, new FixedFraudScreening(FraudScreeningResult.Approved));
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AccountBalanceDbContext>()
            .Set<BalanceReservation>()
            .AsNoTracking()
            .CountAsync(reservation => reservation.TransferId == transferId);
    }

    private static ServiceProvider CreateProvider(
        string connectionString,
        IFraudScreening fraud,
        TimeProvider? timeProvider = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TransferManagement:FraudScreening:MaxTransientAttempts"] = "3",
                ["TransferManagement:FraudScreening:InitialRetryDelaySeconds"] = "2",
                ["TransferManagement:FraudScreening:MaxRetryDelaySeconds"] = "60",
                ["TransferManagement:FraudScreening:LeaseDurationSeconds"] = "30"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddAccountBalanceModule(connectionString);
        services.AddTransferManagementModule(connectionString, configuration);
        services.RemoveAll<IFraudScreening>();
        services.AddSingleton(fraud);
        if (timeProvider is not null)
        {
            services.Replace(ServiceDescriptor.Singleton(timeProvider));
        }

        services.ConfigureManualIntegrationTestHost();
        return services.BuildServiceProvider();
    }

    private sealed record ProcessSnapshot(
        TransferProcessStatus Status,
        TransferProcessAction NextAction,
        int AttemptCount,
        DateTimeOffset? NextAttemptAtUtc,
        Guid CorrelationId);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration) => utcNow += duration;
    }

    private sealed class FixedFraudScreening(FraudScreeningResult result) : IFraudScreening
    {
        public int Invocations { get; private set; }

        public Task<FraudScreeningResult> ScreenAsync(FraudScreeningRequest request, CancellationToken cancellationToken)
        {
            Invocations++;
            return Task.FromResult(result);
        }
    }

    private sealed class SequencedFraudScreening(params FraudScreeningResult[] results) : IFraudScreening
    {
        private int _invocations;

        public int Invocations => _invocations;

        public List<Guid> ObservedTransferIds { get; } = [];

        public Task<FraudScreeningResult> ScreenAsync(FraudScreeningRequest request, CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _invocations) - 1;
            ObservedTransferIds.Add(request.TransferId);
            return Task.FromResult(results[index]);
        }
    }

    private sealed class BlockingFraudExecutionGate(FraudScreeningResult result) : IFraudScreening
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _blocked;
        private int _invocations;

        public Task Entered => _entered.Task;

        public int Invocations => _invocations;

        public async Task<FraudScreeningResult> ScreenAsync(FraudScreeningRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocations);
            if (Interlocked.Exchange(ref _blocked, 1) != 0)
            {
                return result;
            }

            _entered.TrySetResult();
            await _released.Task.WaitAsync(cancellationToken);
            return result;
        }

        public void Release() => _released.TrySetResult();
    }
}

[CollectionDefinition("PostgreSQL fraud screening", DisableParallelization = true)]
public sealed class PostgreSqlFraudScreeningGroup;
