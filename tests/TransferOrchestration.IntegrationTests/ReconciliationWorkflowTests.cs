using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using TransferOrchestration.AccountBalance;
using TransferOrchestration.AccountBalance.Domain.Accounts;
using TransferOrchestration.AccountBalance.Infrastructure.Persistence;
using TransferOrchestration.PaymentNetwork.Contracts;
using TransferOrchestration.TransferManagement;
using TransferOrchestration.TransferManagement.Application.PaymentSubmission;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Application.Reconciliation;
using TransferOrchestration.TransferManagement.Domain.Transfers;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;
using TransferOrchestration.TransferManagement.Infrastructure.Reconciliation;

namespace TransferOrchestration.IntegrationTests;

[Collection("PostgreSQL account reservation")]
public sealed class ReconciliationWorkflowTests : IAsyncLifetime
{
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING")
        ?? throw new InvalidOperationException("TASK-11 PostgreSQL tests require TEST_DATABASE_CONNECTION_STRING.");

    private readonly MutableTimeProvider _clock =
        new(new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero));

    public async Task InitializeAsync()
    {
        await DropSchemasAsync();
        await using var provider = CreateProvider(new RecordingGateway());
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AccountBalanceDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>().Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UnknownToSettledConsumesReservationCompletesTransferAndOutbox()
    {
        var gateway = new RecordingGateway
        {
            SubmissionResult = PaymentSubmissionResult.Timeout,
            StatusResult = PaymentStatusResult.Settled
        };
        var transferId = await SeedUnknownTransferAsync(gateway);

        Assert.Equal(1, await DispatchReconciliationAsync(gateway));
        Assert.Single(gateway.SubmitCalls);
        Assert.Single(gateway.StatusCalls);

        var snapshot = await SnapshotAsync(transferId);
        Assert.Equal(TransferState.Completed, snapshot.TransferState);
        Assert.Equal(ReconciliationStatus.Closed, snapshot.ReconciliationStatus);
        Assert.Equal(BalanceReservationStatus.Consumed, snapshot.ReservationStatus);
        Assert.Equal(400m, snapshot.Available);
        Assert.Equal(0m, snapshot.Reserved);

        await using var context = CreateScopeContext();
        Assert.Single(await context.OutboxMessages.AsNoTracking()
            .Where(item => item.TransferId == transferId.Value).ToListAsync());
    }

    [Fact]
    public async Task UnknownToRejectedReleasesReservationAndClosesReconciliation()
    {
        var gateway = new RecordingGateway
        {
            SubmissionResult = PaymentSubmissionResult.Timeout,
            StatusResult = PaymentStatusResult.Rejected
        };
        var transferId = await SeedUnknownTransferAsync(gateway);

        Assert.Equal(1, await DispatchReconciliationAsync(gateway));

        var snapshot = await SnapshotAsync(transferId);
        Assert.Equal(TransferState.Rejected, snapshot.TransferState);
        Assert.Equal(ReconciliationStatus.Closed, snapshot.ReconciliationStatus);
        Assert.Equal(BalanceReservationStatus.Released, snapshot.ReservationStatus);
        Assert.Equal(500m, snapshot.Available);
        Assert.Equal(0m, snapshot.Reserved);
        Assert.Single(gateway.SubmitCalls);
        Assert.Single(gateway.StatusCalls);
        Assert.Empty(await CreateScopeContext().OutboxMessages.AsNoTracking()
            .Where(item => item.TransferId == transferId.Value).ToListAsync());
    }

    [Fact]
    public async Task UnknownToUnknownPersistsAttemptAndSchedulesNextAttemptWithoutResubmit()
    {
        var gateway = new RecordingGateway
        {
            SubmissionResult = PaymentSubmissionResult.Timeout,
            StatusResult = PaymentStatusResult.Unknown
        };
        var transferId = await SeedUnknownTransferAsync(gateway);

        Assert.Equal(1, await DispatchReconciliationAsync(gateway));
        Assert.Equal(0, await DispatchReconciliationAsync(gateway));

        var after = await SnapshotAsync(transferId);
        Assert.Equal(TransferState.SubmissionStatusUnknown, after.TransferState);
        Assert.Equal(ReconciliationStatus.Active, after.ReconciliationStatus);
        Assert.Equal(1, after.ReconciliationAttemptCount);
        Assert.NotNull(after.ReconciliationNextAttemptAtUtc);
        Assert.True(after.ReconciliationNextAttemptAtUtc > _clock.GetUtcNow());
        Assert.Equal(BalanceReservationStatus.Active, after.ReservationStatus);
        Assert.Single(gateway.SubmitCalls);
        Assert.Single(gateway.StatusCalls);

        _clock.Advance(TimeSpan.FromSeconds(15));
        Assert.Equal(1, await DispatchReconciliationAsync(gateway));
        Assert.Equal(2, (await SnapshotAsync(transferId)).ReconciliationAttemptCount);
        Assert.Equal(2, gateway.StatusCalls.Count);
        Assert.Single(gateway.SubmitCalls);
    }

    [Fact]
    public async Task ThresholdEscalatesToManualReviewRequiredAndKeepsReservationActive()
    {
        var gateway = new RecordingGateway
        {
            SubmissionResult = PaymentSubmissionResult.Timeout,
            StatusResult = PaymentStatusResult.Unknown
        };
        var transferId = await SeedUnknownTransferAsync(gateway, escalationThreshold: 2, retryDelaySeconds: 5);

        Assert.Equal(1, await DispatchReconciliationAsync(gateway, escalationThreshold: 2, retryDelaySeconds: 5));
        _clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(1, await DispatchReconciliationAsync(gateway, escalationThreshold: 2, retryDelaySeconds: 5));

        var snapshot = await SnapshotAsync(transferId);
        Assert.Equal(TransferState.ManualReviewRequired, snapshot.TransferState);
        Assert.Equal(ReconciliationStatus.ManualReviewRequired, snapshot.ReconciliationStatus);
        Assert.Null(snapshot.ReconciliationNextAttemptAtUtc);
        Assert.Equal(BalanceReservationStatus.Active, snapshot.ReservationStatus);
        Assert.Equal(2, gateway.StatusCalls.Count);
        Assert.Single(gateway.SubmitCalls);
    }

    [Fact]
    public async Task RestartRediscoversDueReconciliationWork()
    {
        var gateway = new RecordingGateway
        {
            SubmissionResult = PaymentSubmissionResult.Timeout,
            StatusResult = PaymentStatusResult.Settled
        };
        var transferId = await SeedUnknownTransferAsync(gateway);

        await using var verification = new NpgsqlConnection(_connectionString);
        await verification.OpenAsync();
        await using (var command = verification.CreateCommand())
        {
            command.CommandText =
                """
                SELECT status, next_attempt_at_utc
                FROM transfer_management.reconciliation_records
                WHERE transfer_id = @transfer_id;
                """;
            command.Parameters.AddWithValue("transfer_id", transferId.Value);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.False(reader.IsDBNull(1));
        }

        await using var restarted = CreateProvider(gateway);
        Assert.Equal(1, await restarted.GetRequiredService<IReconciliationDueWorkDispatcher>()
            .DispatchDueAsync(CancellationToken.None));

        var snapshot = await SnapshotAsync(transferId);
        Assert.Equal(TransferState.Completed, snapshot.TransferState);
        Assert.Equal(ReconciliationStatus.Closed, snapshot.ReconciliationStatus);
        Assert.Single(gateway.StatusCalls);
    }

    [Fact]
    public async Task DuplicateSettledStatusIsIdempotent()
    {
        var gateway = new RecordingGateway
        {
            SubmissionResult = PaymentSubmissionResult.Timeout,
            StatusResult = PaymentStatusResult.Settled
        };
        var transferId = await SeedUnknownTransferAsync(gateway);

        Assert.Equal(1, await DispatchReconciliationAsync(gateway));
        gateway.StatusResult = PaymentStatusResult.Settled;
        Assert.Equal(0, await DispatchReconciliationAsync(gateway));

        var snapshot = await SnapshotAsync(transferId);
        Assert.Equal(TransferState.Completed, snapshot.TransferState);
        Assert.Equal(BalanceReservationStatus.Consumed, snapshot.ReservationStatus);
        Assert.Single(gateway.StatusCalls);
        await using var context = CreateScopeContext();
        Assert.Single(await context.OutboxMessages.AsNoTracking()
            .Where(item => item.TransferId == transferId.Value).ToListAsync());
    }

    [Fact]
    public async Task ConcurrentWorkersDoNotProcessSameDueRecordTwice()
    {
        var gateway = new RecordingGateway
        {
            SubmissionResult = PaymentSubmissionResult.Timeout,
            StatusResult = PaymentStatusResult.Settled
        };
        await SeedUnknownTransferAsync(gateway);

        await using var provider = CreateProvider(gateway);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.StatusGate = gate;

        await using var firstScope = provider.CreateAsyncScope();
        var firstDispatch = firstScope.ServiceProvider.GetRequiredService<IReconciliationDueWorkDispatcher>()
            .DispatchDueAsync(CancellationToken.None);
        await gateway.StatusStarted.Task;

        await using var secondScope = provider.CreateAsyncScope();
        var secondCount = await secondScope.ServiceProvider.GetRequiredService<IReconciliationDueWorkDispatcher>()
            .DispatchDueAsync(CancellationToken.None);

        gate.TrySetResult();
        await firstDispatch;

        Assert.Equal(0, secondCount);
        Assert.Single(gateway.StatusCalls);
        Assert.Single(gateway.SubmitCalls);
    }

    [Fact]
    public async Task EnquiryFailureSchedulesRetryWithoutLosingRecord()
    {
        var gateway = new RecordingGateway
        {
            SubmissionResult = PaymentSubmissionResult.Timeout,
            ThrowStatusException = true
        };
        var transferId = await SeedUnknownTransferAsync(gateway, retryDelaySeconds: 8);

        Assert.Equal(1, await DispatchReconciliationAsync(gateway, retryDelaySeconds: 8));
        var snapshot = await SnapshotAsync(transferId);
        Assert.Equal(ReconciliationStatus.Active, snapshot.ReconciliationStatus);
        Assert.Equal(1, snapshot.ReconciliationAttemptCount);
        Assert.NotNull(snapshot.ReconciliationNextAttemptAtUtc);

        gateway.ThrowStatusException = false;
        gateway.StatusResult = PaymentStatusResult.Settled;
        _clock.Advance(TimeSpan.FromSeconds(9));
        Assert.Equal(1, await DispatchReconciliationAsync(gateway, retryDelaySeconds: 8));
        Assert.Equal(TransferState.Completed, (await SnapshotAsync(transferId)).TransferState);
    }

    [Fact]
    public async Task EnquiryFailureTruncatesLongErrorMessageBeforePersisting()
    {
        var longError = new string('x', ReconciliationRecord.MaxLastErrorLength + 100);
        var gateway = new RecordingGateway
        {
            SubmissionResult = PaymentSubmissionResult.Timeout,
            ThrowStatusException = true,
            StatusExceptionMessage = longError
        };
        var transferId = await SeedUnknownTransferAsync(gateway, retryDelaySeconds: 8);

        Assert.Equal(1, await DispatchReconciliationAsync(gateway, retryDelaySeconds: 8));

        await using var context = CreateScopeContext();
        var reconciliation = await context.ReconciliationRecords.AsNoTracking()
            .SingleAsync(x => x.TransferId == transferId);
        Assert.Equal(ReconciliationStatus.Active, reconciliation.Status);
        Assert.Equal(ReconciliationRecord.MaxLastErrorLength, reconciliation.LastError!.Length);
        Assert.Equal(longError[..ReconciliationRecord.MaxLastErrorLength], reconciliation.LastError);
    }

    [Fact]
    public async Task EnquiryFailureThresholdEscalatesToManualReviewRequired()
    {
        var gateway = new RecordingGateway
        {
            SubmissionResult = PaymentSubmissionResult.Timeout,
            ThrowStatusException = true
        };
        var transferId = await SeedUnknownTransferAsync(gateway, escalationThreshold: 2, retryDelaySeconds: 5);

        Assert.Equal(1, await DispatchReconciliationAsync(gateway, escalationThreshold: 2, retryDelaySeconds: 5));
        _clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(1, await DispatchReconciliationAsync(gateway, escalationThreshold: 2, retryDelaySeconds: 5));

        var snapshot = await SnapshotAsync(transferId);
        Assert.Equal(TransferState.ManualReviewRequired, snapshot.TransferState);
        Assert.Equal(ReconciliationStatus.ManualReviewRequired, snapshot.ReconciliationStatus);
        Assert.Null(snapshot.ReconciliationNextAttemptAtUtc);
        Assert.Equal(BalanceReservationStatus.Active, snapshot.ReservationStatus);
        Assert.Equal(2, gateway.StatusCalls.Count);
        Assert.Single(gateway.SubmitCalls);
    }

    [Fact]
    public async Task ExpiredClaimBecomesRecoverable()
    {
        var gateway = new RecordingGateway
        {
            SubmissionResult = PaymentSubmissionResult.Timeout,
            StatusResult = PaymentStatusResult.Settled
        };
        var transferId = await SeedUnknownTransferAsync(gateway, leaseDurationSeconds: 1);

        await using var provider = CreateProvider(gateway, leaseDurationSeconds: 1);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.StatusGate = gate;

        await using var blockedScope = provider.CreateAsyncScope();
        var blockedDispatch = blockedScope.ServiceProvider.GetRequiredService<IReconciliationDueWorkDispatcher>()
            .DispatchDueAsync(CancellationToken.None);
        await gateway.StatusStarted.Task;
        _clock.Advance(TimeSpan.FromSeconds(2));
        gate.TrySetResult();
        await blockedDispatch;

        Assert.Single(gateway.StatusCalls);
        Assert.Equal(TransferState.Completed, (await SnapshotAsync(transferId)).TransferState);
    }

    [Fact]
    public async Task NonDueRecordIsNotProcessed()
    {
        var gateway = new RecordingGateway
        {
            SubmissionResult = PaymentSubmissionResult.Timeout,
            StatusResult = PaymentStatusResult.Unknown
        };
        await SeedUnknownTransferAsync(gateway, retryDelaySeconds: 60);
        Assert.Equal(1, await DispatchReconciliationAsync(gateway, retryDelaySeconds: 60));

        Assert.Equal(0, await DispatchReconciliationAsync(gateway, retryDelaySeconds: 60));
        Assert.Single(gateway.StatusCalls);
    }

    [Fact]
    public async Task BoundedBatchSizeLimitsClaimedWork()
    {
        var gateway = new RecordingGateway
        {
            SubmissionResult = PaymentSubmissionResult.Timeout,
            StatusResult = PaymentStatusResult.Unknown
        };
        await SeedUnknownTransferAsync(gateway);
        await SeedUnknownTransferAsync(gateway);
        await SeedUnknownTransferAsync(gateway);

        await using var provider = CreateProvider(gateway, batchSize: 2);
        Assert.Equal(2, await provider.GetRequiredService<IReconciliationDueWorkDispatcher>()
            .DispatchDueAsync(CancellationToken.None));
        Assert.Equal(2, gateway.StatusCalls.Count);
    }

    [Fact]
    public void InvalidReconciliationConfigurationFailsFastOnStartup()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TransferManagement:Reconciliation:EscalationAttemptThreshold"] = "0"
            })
            .Build();
        services.AddOptions<ReconciliationOptions>()
            .Bind(configuration.GetSection("TransferManagement:Reconciliation"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        using var provider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<ReconciliationOptions>>().Value);

        Assert.Contains("EscalationAttemptThreshold", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MigrationCreatesReconciliationRecordsTableOnPostgreSql()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_schema = 'transfer_management'
                  AND table_name = 'reconciliation_records');
            """;
        Assert.True((bool)(await command.ExecuteScalarAsync())!);
    }

    private async Task<TransferId> SeedUnknownTransferAsync(
        RecordingGateway gateway,
        int escalationThreshold = 5,
        int retryDelaySeconds = 10,
        int leaseDurationSeconds = 30)
    {
        var accountId = Guid.NewGuid();
        var now = _clock.GetUtcNow();
        var transfer = Transfer.Create(accountId, Guid.NewGuid(), 100m, "GBP", TransferType.DomesticInterbank, now);
        transfer.Submit(now);
        transfer.RequestAuthorisation(now);
        transfer.Authorise(now);
        transfer.BeginFraudScreening(now);
        transfer.RequestBalanceReservation(now);
        var process = TransferProcessState.Create(transfer.Id, Guid.NewGuid(), now);
        process.Schedule(TransferProcessAction.ReserveBalance, now, now);

        await using var provider = CreateProvider(gateway, escalationThreshold, retryDelaySeconds, leaseDurationSeconds);
        await using var scope = provider.CreateAsyncScope();
        var accountContext = scope.ServiceProvider.GetRequiredService<AccountBalanceDbContext>();
        accountContext.Accounts.Add(Account.Create(accountId, "GBP", 500m, AccountStatus.Active));
        await accountContext.SaveChangesAsync();
        var transferContext = scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>();
        transferContext.AddRange(transfer, process);
        await transferContext.SaveChangesAsync();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ITransferProcessDueWorkDispatcher>()
            .DispatchDueAsync(CancellationToken.None));
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<IPaymentSubmissionDueWorkDispatcher>()
            .DispatchDueAsync(CancellationToken.None));
        return transfer.Id;
    }

    private async Task<int> DispatchReconciliationAsync(
        RecordingGateway gateway,
        int escalationThreshold = 5,
        int retryDelaySeconds = 10,
        int leaseDurationSeconds = 30)
    {
        await using var provider = CreateProvider(gateway, escalationThreshold, retryDelaySeconds, leaseDurationSeconds);
        return await provider.GetRequiredService<IReconciliationDueWorkDispatcher>()
            .DispatchDueAsync(CancellationToken.None);
    }

    private async Task<ReconciliationSnapshot> SnapshotAsync(TransferId transferId)
    {
        await using var provider = CreateProvider(new RecordingGateway());
        await using var scope = provider.CreateAsyncScope();
        var transferContext = scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>();
        var transfer = await transferContext.Transfers.AsNoTracking().SingleAsync(x => x.Id == transferId);
        var process = await transferContext.TransferProcessStates.AsNoTracking().SingleAsync(x => x.TransferId == transferId);
        var reconciliation = await transferContext.ReconciliationRecords.AsNoTracking()
            .SingleAsync(x => x.TransferId == transferId);
        var accountContext = scope.ServiceProvider.GetRequiredService<AccountBalanceDbContext>();
        var account = await accountContext.Accounts.Include(x => x.Reservations).AsNoTracking()
            .SingleAsync(x => x.Id == new AccountId(transfer.SourceAccountId));
        var reservation = Assert.Single(account.Reservations);
        return new ReconciliationSnapshot(
            transfer.State,
            process.Status,
            process.NextAction,
            reconciliation.Status,
            reconciliation.AttemptCount,
            reconciliation.NextAttemptAtUtc,
            account.AvailableBalance,
            account.ReservedBalance,
            reservation.Status);
    }

    private TransferManagementDbContext CreateScopeContext()
    {
        var provider = CreateProvider(new RecordingGateway());
        return provider.GetRequiredService<TransferManagementDbContext>();
    }

    private ServiceProvider CreateProvider(
        RecordingGateway gateway,
        int escalationThreshold = 5,
        int retryDelaySeconds = 10,
        int batchSize = 20,
        int leaseDurationSeconds = 30)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TransferManagement:Reconciliation:EscalationAttemptThreshold"] =
                    escalationThreshold.ToString(CultureInfo.InvariantCulture),
                ["TransferManagement:Reconciliation:RetryDelaySeconds"] =
                    retryDelaySeconds.ToString(CultureInfo.InvariantCulture),
                ["TransferManagement:Reconciliation:BatchSize"] =
                    batchSize.ToString(CultureInfo.InvariantCulture),
                ["TransferManagement:Reconciliation:LeaseDurationSeconds"] =
                    leaseDurationSeconds.ToString(CultureInfo.InvariantCulture),
                ["TransferManagement:Reconciliation:PollIntervalMilliseconds"] = "1000"
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddAccountBalanceModule(_connectionString);
        services.AddTransferManagementModule(_connectionString, configuration);
        services.Replace(ServiceDescriptor.Singleton<IPaymentNetworkGateway>(gateway));
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(_clock));
        return services.BuildServiceProvider();
    }

    private async Task DropSchemasAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "DROP SCHEMA IF EXISTS transfer_management CASCADE; DROP SCHEMA IF EXISTS account_balance CASCADE;";
        await command.ExecuteNonQueryAsync();
    }

    private sealed class RecordingGateway : IPaymentNetworkGateway
    {
        public PaymentSubmissionResult SubmissionResult { get; init; } = PaymentSubmissionResult.Accepted;
        public PaymentStatusResult StatusResult { get; set; } = PaymentStatusResult.Unknown;
        public bool ThrowStatusException { get; set; }
        public string StatusExceptionMessage { get; set; } = "Simulated status enquiry failure.";
        public TaskCompletionSource? StatusGate { get; set; }
        public TaskCompletionSource StatusStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<PaymentSubmissionRequest> SubmitCalls { get; } = [];
        public List<NetworkSubmissionReference> StatusCalls { get; } = [];

        public NetworkSubmissionReference CreateSubmissionReference(Guid transferId) =>
            new($"TEST-{transferId:N}".ToUpperInvariant());

        public Task<PaymentSubmissionResult> SubmitAsync(
            PaymentSubmissionRequest request,
            CancellationToken cancellationToken)
        {
            SubmitCalls.Add(request);
            return Task.FromResult(SubmissionResult);
        }

        public async Task<PaymentStatusResult> GetStatusAsync(
            NetworkSubmissionReference reference,
            CancellationToken cancellationToken)
        {
            StatusCalls.Add(reference);
            StatusStarted.TrySetResult();
            if (StatusGate is not null)
            {
                await StatusGate.Task.WaitAsync(cancellationToken);
            }

            if (ThrowStatusException)
            {
                throw new InvalidOperationException(StatusExceptionMessage);
            }

            return StatusResult;
        }
    }

    private sealed record ReconciliationSnapshot(
        TransferState TransferState,
        TransferProcessStatus ProcessStatus,
        TransferProcessAction NextAction,
        ReconciliationStatus ReconciliationStatus,
        int ReconciliationAttemptCount,
        DateTimeOffset? ReconciliationNextAttemptAtUtc,
        decimal Available,
        decimal Reserved,
        BalanceReservationStatus ReservationStatus);

    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _utcNow = start;

        public void Advance(TimeSpan amount) => _utcNow += amount;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
