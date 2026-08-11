using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using TransferOrchestration.AccountBalance;
using TransferOrchestration.AccountBalance.Domain.Accounts;
using TransferOrchestration.AccountBalance.Infrastructure.Persistence;
using TransferOrchestration.PaymentNetwork.Contracts;
using TransferOrchestration.TransferManagement;
using TransferOrchestration.TransferManagement.Application.PaymentSubmission;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Domain.Transfers;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;

namespace TransferOrchestration.IntegrationTests;

[Collection("PostgreSQL account reservation")]
public sealed class PaymentSubmissionWorkflowTests : IAsyncLifetime
{
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING")
        ?? throw new InvalidOperationException("Destructive PostgreSQL tests require TEST_DATABASE_CONNECTION_STRING.");

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP SCHEMA IF EXISTS transfer_management CASCADE; DROP SCHEMA IF EXISTS account_balance CASCADE;";
        await command.ExecuteNonQueryAsync();
        await using var provider = CreateProvider(new RecordingGateway());
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AccountBalanceDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>().Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AcceptedIsSettlementPendingAndCannotBeSubmittedAgain()
    {
        var gateway = new RecordingGateway { SubmissionResult = PaymentSubmissionResult.Accepted };
        var transferId = await SeedReservedTransferAsync(TransferType.DomesticInterbank, gateway);

        Assert.Equal(1, await DispatchPaymentAsync(gateway));
        Assert.Equal(0, await DispatchPaymentAsync(gateway));

        var snapshot = await SnapshotAsync(transferId);
        Assert.Equal(TransferState.SettlementPending, snapshot.TransferState);
        Assert.Equal(TransferProcessStatus.Waiting, snapshot.ProcessStatus);
        Assert.Equal(TransferProcessAction.None, snapshot.NextAction);
        Assert.Single(gateway.SubmitCalls);
        Assert.Equal(snapshot.Reference, gateway.SubmitCalls.Single().Reference.Value);
        AssertReservationActive(snapshot);
    }

    [Fact]
    public async Task RejectionRequestsReleaseWithoutSubmittingTwice()
    {
        var gateway = new RecordingGateway { SubmissionResult = PaymentSubmissionResult.Rejected };
        var transferId = await SeedReservedTransferAsync(TransferType.DomesticInterbank, gateway);

        Assert.Equal(1, await DispatchPaymentAsync(gateway));
        Assert.Equal(0, await DispatchPaymentAsync(gateway));

        var snapshot = await SnapshotAsync(transferId);
        Assert.Equal(TransferState.Rejected, snapshot.TransferState);
        Assert.Equal(TransferProcessAction.ReleaseReservation, snapshot.NextAction);
        Assert.Single(gateway.SubmitCalls);
        AssertReservationActive(snapshot);
    }

    [Fact]
    public async Task TimeoutPersistsUnknownAndRestartUsesSameReferenceWithoutResubmit()
    {
        var gateway = new RecordingGateway { SubmissionResult = PaymentSubmissionResult.Timeout };
        var transferId = await SeedReservedTransferAsync(TransferType.DomesticInterbank, gateway);

        Assert.Equal(1, await DispatchPaymentAsync(gateway));
        var first = await SnapshotAsync(transferId);
        AssertUnknown(first, gateway);

        await using (var restarted = CreateProvider(gateway))
        await using (var scope = restarted.CreateAsyncScope())
        {
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IPaymentSubmissionDueWorkDispatcher>()
                .DispatchDueAsync(CancellationToken.None));
            var reference = new NetworkSubmissionReference(first.Reference!);
            await scope.ServiceProvider.GetRequiredService<IPaymentNetworkGateway>()
                .GetStatusAsync(reference, CancellationToken.None);
        }

        Assert.Single(gateway.SubmitCalls);
        Assert.Equal(first.Reference, Assert.Single(gateway.StatusCalls).Value);
        AssertUnknown(await SnapshotAsync(transferId), gateway);
    }

    [Fact]
    public async Task ThrownAmbiguousProviderExceptionIsDurablyUnknownAfterFreshReload()
    {
        var gateway = new RecordingGateway { ThrowAmbiguousException = true };
        var transferId = await SeedReservedTransferAsync(TransferType.DomesticInterbank, gateway);

        Assert.Equal(1, await DispatchPaymentAsync(gateway));

        // SnapshotAsync creates a new provider and DbContext, proving the outcome was
        // committed before the dispatcher returned rather than retained in tracking.
        var snapshot = await SnapshotAsync(transferId);
        AssertUnknown(snapshot, gateway);
        Assert.Equal(snapshot.Reference, gateway.SubmitCalls.Single().Reference.Value);
    }

    [Fact]
    public async Task CancellationShapedProviderTimeoutWithoutCallerCancellationIsDurablyUnknown()
    {
        var gateway = new RecordingGateway { ThrowCancellation = true };
        var transferId = await SeedReservedTransferAsync(TransferType.DomesticInterbank, gateway);

        Assert.Equal(1, await DispatchPaymentAsync(gateway));
        AssertUnknown(await SnapshotAsync(transferId), gateway);
        Assert.Equal(0, await DispatchPaymentAsync(gateway));
        Assert.Single(gateway.SubmitCalls);
    }

    [Fact]
    public async Task CallerCancellationAfterSubmissionStartsIsPersistedBeforeCancellationEscapes()
    {
        var gateway = new RecordingGateway { WaitForCallerCancellation = true };
        var transferId = await SeedReservedTransferAsync(TransferType.DomesticInterbank, gateway);
        using var cancellation = new CancellationTokenSource();

        var dispatch = DispatchPaymentAsync(gateway, cancellation.Token);
        await gateway.SubmissionStarted.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatch);
        AssertUnknown(await SnapshotAsync(transferId), gateway);
        Assert.Equal(0, await DispatchPaymentAsync(gateway));
        Assert.Single(gateway.SubmitCalls);
    }

    [Theory]
    [InlineData(PaymentSubmissionResult.Accepted)]
    [InlineData(PaymentSubmissionResult.Rejected)]
    [InlineData(PaymentSubmissionResult.Timeout)]
    public async Task ReturnedOutcomeIsPersistedBeforePostSubmissionCallerCancellationEscapes(
        PaymentSubmissionResult submissionResult)
    {
        using var cancellation = new CancellationTokenSource();
        var gateway = new RecordingGateway
        {
            SubmissionResult = submissionResult,
            BeforeReturningSubmissionResult = cancellation.Cancel
        };
        var transferId = await SeedReservedTransferAsync(TransferType.DomesticInterbank, gateway);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DispatchPaymentAsync(gateway, cancellation.Token));

        var snapshot = await SnapshotAsync(transferId);
        switch (submissionResult)
        {
            case PaymentSubmissionResult.Accepted:
                Assert.Equal(TransferState.SettlementPending, snapshot.TransferState);
                Assert.Equal(TransferProcessStatus.Waiting, snapshot.ProcessStatus);
                Assert.Equal(TransferProcessAction.None, snapshot.NextAction);
                break;
            case PaymentSubmissionResult.Rejected:
                Assert.Equal(TransferState.Rejected, snapshot.TransferState);
                Assert.Equal(TransferProcessStatus.Active, snapshot.ProcessStatus);
                Assert.Equal(TransferProcessAction.ReleaseReservation, snapshot.NextAction);
                break;
            case PaymentSubmissionResult.Timeout:
                Assert.Equal(TransferState.SubmissionStatusUnknown, snapshot.TransferState);
                Assert.Equal(TransferProcessStatus.Active, snapshot.ProcessStatus);
                Assert.Equal(TransferProcessAction.EnquirePaymentStatus, snapshot.NextAction);
                break;
            default:
                throw new InvalidOperationException("Unsupported test submission result.");
        }

        AssertReservationActive(snapshot);
        Assert.Single(gateway.SubmitCalls);
        Assert.Equal(0, await DispatchPaymentAsync(gateway));
        Assert.Single(gateway.SubmitCalls);
    }

    [Fact]
    public async Task Task08MigrationUpThenDownRestoresOnlyExactTask07DomesticHandoffRows()
    {
        await using (var connection = new NpgsqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            await using var drop = connection.CreateCommand();
            drop.CommandText = "DROP SCHEMA IF EXISTS transfer_management CASCADE;";
            await drop.ExecuteNonQueryAsync();
        }

        await using var provider = CreateProvider(new RecordingGateway());
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>();
            await context.GetService<IMigrator>()
                .MigrateAsync("20260811170000_AddCumulativeDailyTransferUsage");
        }

        var upgradedId = Guid.NewGuid();
        var internalId = Guid.NewGuid();
        var unrelatedId = Guid.NewGuid();
        await SeedTask07ProcessRowsAsync(upgradedId, internalId, unrelatedId);

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>();
            await context.Database.MigrateAsync();
        }

        await using var verification = new NpgsqlConnection(_connectionString);
        await verification.OpenAsync();
        var rows = new Dictionary<Guid, MigratedProcessRow>();
        await using (var command = verification.CreateCommand())
        {
            command.CommandText =
                """
                SELECT transfer_id, status, current_step, next_action,
                       next_attempt_at_utc, version, attempt_count, network_submission_reference
                FROM transfer_management.transfer_process_states
                WHERE transfer_id = ANY(@ids);
                """;
            command.Parameters.AddWithValue("ids", new[] { upgradedId, internalId, unrelatedId });
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(reader.GetGuid(0), new MigratedProcessRow(
                    reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
                    reader.GetInt64(5), reader.GetInt32(6), reader.IsDBNull(7) ? null : reader.GetString(7)));
            }
        }

        Assert.Equal(new MigratedProcessRow(
            "Active", "ActionScheduled", "SubmitToPaymentNetwork", rows[upgradedId].DueAtUtc,
            8, 3, null), rows[upgradedId]);
        Assert.NotNull(rows[upgradedId].DueAtUtc);
        Assert.Equal(new MigratedProcessRow(
            "Waiting", "WaitingForOutcome", "None", null, 7, 3, null), rows[internalId]);
        Assert.Equal(new MigratedProcessRow(
            "Active", "ActionScheduled", "ReserveBalance", rows[unrelatedId].DueAtUtc,
            7, 3, null), rows[unrelatedId]);
        Assert.NotNull(rows[unrelatedId].DueAtUtc);

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>();
            await context.GetService<IMigrator>()
                .MigrateAsync("20260811170000_AddCumulativeDailyTransferUsage");
        }

        var downgradedRows = await ReadTask07RowsAsync(upgradedId, internalId, unrelatedId);
        Assert.Equal(new Task07ProcessRow("Waiting", "WaitingForOutcome", "None", null, 9, 3),
            downgradedRows[upgradedId]);
        Assert.Equal(new Task07ProcessRow("Waiting", "WaitingForOutcome", "None", null, 7, 3),
            downgradedRows[internalId]);
        Assert.Equal(new Task07ProcessRow(
            "Active", "ActionScheduled", "ReserveBalance", downgradedRows[unrelatedId].DueAtUtc, 7, 3),
            downgradedRows[unrelatedId]);
        Assert.NotNull(downgradedRows[unrelatedId].DueAtUtc);
        Assert.False(await NetworkSubmissionReferenceColumnExistsAsync());
    }

    [Fact]
    public async Task Task08MigrationDownRefusesToDiscardExternalSubmissionStateAndRollsBack()
    {
        var gateway = new RecordingGateway { SubmissionResult = PaymentSubmissionResult.Timeout };
        var transferId = await SeedReservedTransferAsync(TransferType.DomesticInterbank, gateway);
        Assert.Equal(1, await DispatchPaymentAsync(gateway));
        var before = await SnapshotAsync(transferId);
        AssertUnknown(before, gateway);

        await using var provider = CreateProvider(gateway);
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>();
            var exception = await Assert.ThrowsAnyAsync<Exception>(() => context.GetService<IMigrator>()
                .MigrateAsync("20260811170000_AddCumulativeDailyTransferUsage"));
            Assert.Contains(
                "Cannot downgrade TASK-08 while external payment submission state exists.",
                exception.ToString(),
                StringComparison.Ordinal);
        }

        Assert.True(await NetworkSubmissionReferenceColumnExistsAsync());
        Assert.True(await Task08MigrationIsAppliedAsync());
        Assert.Equal(before, await SnapshotAsync(transferId));
    }

    [Fact]
    public async Task Task08MigrationDownRestoresNewPreSubmissionDomesticHandoff()
    {
        var transferId = await SeedReservedTransferAsync(TransferType.DomesticInterbank, new RecordingGateway());
        var before = await ReadCurrentProcessRowAsync(transferId.Value);
        Assert.Equal("Active", before.Status);
        Assert.Equal("ActionScheduled", before.CurrentStep);
        Assert.Equal("SubmitToPaymentNetwork", before.NextAction);
        Assert.Null(before.Reference);

        await using var provider = CreateProvider(new RecordingGateway());
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>();
            await context.GetService<IMigrator>()
                .MigrateAsync("20260811170000_AddCumulativeDailyTransferUsage");
        }

        var row = (await ReadTask07RowsAsync(transferId.Value))[transferId.Value];
        Assert.Equal("Waiting", row.Status);
        Assert.Equal("WaitingForOutcome", row.CurrentStep);
        Assert.Equal("None", row.NextAction);
        Assert.Null(row.DueAtUtc);
        Assert.Equal(before.Version + 1, row.Version);
        Assert.Equal(before.AttemptCount, row.AttemptCount);
        Assert.False(await NetworkSubmissionReferenceColumnExistsAsync());
    }

    [Fact]
    public async Task InternalBankNeverRoutesToOrCallsPaymentNetwork()
    {
        var gateway = new RecordingGateway();
        var transferId = await SeedReservedTransferAsync(TransferType.InternalBank, gateway);

        Assert.Equal(0, await DispatchPaymentAsync(gateway));

        var snapshot = await SnapshotAsync(transferId);
        Assert.Equal(TransferState.BalanceReserved, snapshot.TransferState);
        Assert.Equal(TransferProcessStatus.Waiting, snapshot.ProcessStatus);
        Assert.Equal(TransferProcessAction.None, snapshot.NextAction);
        Assert.Null(snapshot.Reference);
        Assert.Empty(gateway.SubmitCalls);
        Assert.Empty(gateway.StatusCalls);
        AssertReservationActive(snapshot);
    }

    private async Task<TransferId> SeedReservedTransferAsync(TransferType type, RecordingGateway gateway)
    {
        var accountId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var transfer = Transfer.Create(accountId, Guid.NewGuid(), 100m, "GBP", type, now);
        transfer.Submit(now);
        transfer.RequestAuthorisation(now);
        transfer.Authorise(now);
        transfer.BeginFraudScreening(now);
        transfer.RequestBalanceReservation(now);
        var process = TransferProcessState.Create(transfer.Id, Guid.NewGuid(), now);
        process.Schedule(TransferProcessAction.ReserveBalance, now, now);

        await using var provider = CreateProvider(gateway);
        await using var scope = provider.CreateAsyncScope();
        var accountContext = scope.ServiceProvider.GetRequiredService<AccountBalanceDbContext>();
        accountContext.Accounts.Add(Account.Create(accountId, "GBP", 500m, AccountStatus.Active));
        await accountContext.SaveChangesAsync();
        var transferContext = scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>();
        transferContext.AddRange(transfer, process);
        await transferContext.SaveChangesAsync();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ITransferProcessDueWorkDispatcher>()
            .DispatchDueAsync(CancellationToken.None));
        return transfer.Id;
    }

    private async Task<int> DispatchPaymentAsync(
        RecordingGateway gateway,
        CancellationToken cancellationToken = default)
    {
        await using var provider = CreateProvider(gateway);
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPaymentSubmissionDueWorkDispatcher>()
            .DispatchDueAsync(cancellationToken);
    }

    private async Task SeedTask07ProcessRowsAsync(Guid upgradedId, Guid internalId, Guid unrelatedId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO transfer_management.transfers
                (id, source_account_id, destination_account_id, amount, currency, type, state,
                 created_at_utc, updated_at_utc, version)
            VALUES
                (@upgraded, gen_random_uuid(), gen_random_uuid(), 100, 'GBP', 'DomesticInterbank', 'BalanceReserved', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 7),
                (@internal, gen_random_uuid(), gen_random_uuid(), 100, 'GBP', 'InternalBank', 'BalanceReserved', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 7),
                (@unrelated, gen_random_uuid(), gen_random_uuid(), 100, 'GBP', 'DomesticInterbank', 'BalanceReserved', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 7);

            INSERT INTO transfer_management.transfer_process_states
                (transfer_id, correlation_id, status, current_step, next_action, attempt_count,
                 next_attempt_at_utc, version, created_at_utc, updated_at_utc)
            VALUES
                (@upgraded, gen_random_uuid(), 'Waiting', 'WaitingForOutcome', 'None', 3, NULL, 7, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
                (@internal, gen_random_uuid(), 'Waiting', 'WaitingForOutcome', 'None', 3, NULL, 7, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
                (@unrelated, gen_random_uuid(), 'Active', 'ActionScheduled', 'ReserveBalance', 3, CURRENT_TIMESTAMP, 7, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
            """;
        command.Parameters.AddWithValue("upgraded", upgradedId);
        command.Parameters.AddWithValue("internal", internalId);
        command.Parameters.AddWithValue("unrelated", unrelatedId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<Dictionary<Guid, Task07ProcessRow>> ReadTask07RowsAsync(params Guid[] ids)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT transfer_id, status, current_step, next_action, next_attempt_at_utc, version, attempt_count
            FROM transfer_management.transfer_process_states
            WHERE transfer_id = ANY(@ids);
            """;
        command.Parameters.AddWithValue("ids", ids);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new Dictionary<Guid, Task07ProcessRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetGuid(0), new Task07ProcessRow(
                reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetInt64(5), reader.GetInt32(6)));
        }

        return rows;
    }

    private async Task<MigratedProcessRow> ReadCurrentProcessRowAsync(Guid transferId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT status, current_step, next_action, next_attempt_at_utc, version, attempt_count,
                   network_submission_reference
            FROM transfer_management.transfer_process_states
            WHERE transfer_id = @transfer_id;
            """;
        command.Parameters.AddWithValue("transfer_id", transferId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new MigratedProcessRow(
            reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetInt64(4), reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetString(6));
    }

    private async Task<bool> NetworkSubmissionReferenceColumnExistsAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'transfer_management'
                  AND table_name = 'transfer_process_states'
                  AND column_name = 'network_submission_reference');
            """;
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private async Task<bool> Task08MigrationIsAppliedAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1 FROM transfer_management."__EFMigrationsHistory"
                WHERE "MigrationId" = '20260811190000_AddNetworkSubmissionReference');
            """;
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private async Task<WorkflowSnapshot> SnapshotAsync(TransferId transferId)
    {
        await using var provider = CreateProvider(new RecordingGateway());
        await using var scope = provider.CreateAsyncScope();
        var transferContext = scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>();
        var transfer = await transferContext.Transfers.AsNoTracking().SingleAsync(x => x.Id == transferId);
        var process = await transferContext.TransferProcessStates.AsNoTracking().SingleAsync(x => x.TransferId == transferId);
        var accountContext = scope.ServiceProvider.GetRequiredService<AccountBalanceDbContext>();
        var account = await accountContext.Accounts.Include(x => x.Reservations).AsNoTracking()
            .SingleAsync(x => x.Id == new AccountId(transfer.SourceAccountId));
        var reservation = Assert.Single(account.Reservations);
        return new WorkflowSnapshot(
            transfer.State, process.Status, process.NextAction, process.NetworkSubmissionReference,
            account.AvailableBalance, account.ReservedBalance, reservation.Status);
    }

    private ServiceProvider CreateProvider(RecordingGateway gateway)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddAccountBalanceModule(_connectionString);
        services.AddTransferManagementModule(_connectionString, configuration);
        services.Replace(ServiceDescriptor.Singleton<IPaymentNetworkGateway>(gateway));
        return services.BuildServiceProvider();
    }

    private static void AssertUnknown(WorkflowSnapshot snapshot, RecordingGateway gateway)
    {
        Assert.Equal(TransferState.SubmissionStatusUnknown, snapshot.TransferState);
        Assert.Equal(TransferProcessStatus.Active, snapshot.ProcessStatus);
        Assert.Equal(TransferProcessAction.EnquirePaymentStatus, snapshot.NextAction);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Reference));
        Assert.Single(gateway.SubmitCalls);
        AssertReservationActive(snapshot);
    }

    private static void AssertReservationActive(WorkflowSnapshot snapshot)
    {
        Assert.Equal(400m, snapshot.Available);
        Assert.Equal(100m, snapshot.Reserved);
        Assert.Equal(BalanceReservationStatus.Active, snapshot.ReservationStatus);
    }

    private sealed class RecordingGateway : IPaymentNetworkGateway
    {
        public PaymentSubmissionResult SubmissionResult { get; init; } = PaymentSubmissionResult.Accepted;
        public bool ThrowAmbiguousException { get; init; }
        public bool ThrowCancellation { get; init; }
        public bool WaitForCallerCancellation { get; init; }
        public Action? BeforeReturningSubmissionResult { get; init; }
        public TaskCompletionSource SubmissionStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<PaymentSubmissionRequest> SubmitCalls { get; } = [];
        public List<NetworkSubmissionReference> StatusCalls { get; } = [];

        public NetworkSubmissionReference CreateSubmissionReference(Guid transferId) =>
            new($"TEST-{transferId:N}".ToUpperInvariant());

        public async Task<PaymentSubmissionResult> SubmitAsync(
            PaymentSubmissionRequest request,
            CancellationToken cancellationToken)
        {
            SubmitCalls.Add(request);
            SubmissionStarted.TrySetResult();
            if (WaitForCallerCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (ThrowCancellation)
            {
                throw new TaskCanceledException("Provider timed out after submission began.");
            }

            if (ThrowAmbiguousException)
            {
                throw new TimeoutException("Provider outcome is ambiguous.");
            }

            BeforeReturningSubmissionResult?.Invoke();
            return SubmissionResult;
        }

        public Task<PaymentStatusResult> GetStatusAsync(NetworkSubmissionReference reference, CancellationToken cancellationToken)
        {
            StatusCalls.Add(reference);
            return Task.FromResult(PaymentStatusResult.Unknown);
        }
    }

    private sealed record WorkflowSnapshot(
        TransferState TransferState,
        TransferProcessStatus ProcessStatus,
        TransferProcessAction NextAction,
        string? Reference,
        decimal Available,
        decimal Reserved,
        BalanceReservationStatus ReservationStatus);

    private sealed record MigratedProcessRow(
        string Status,
        string CurrentStep,
        string NextAction,
        DateTimeOffset? DueAtUtc,
        long Version,
        int AttemptCount,
        string? Reference);

    private sealed record Task07ProcessRow(
        string Status,
        string CurrentStep,
        string NextAction,
        DateTimeOffset? DueAtUtc,
        long Version,
        int AttemptCount);
}
