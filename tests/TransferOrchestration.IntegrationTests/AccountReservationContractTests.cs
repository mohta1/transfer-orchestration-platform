using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using TransferOrchestration.AccountBalance;
using TransferOrchestration.AccountBalance.Application.Reservations;
using TransferOrchestration.AccountBalance.Contracts;
using TransferOrchestration.AccountBalance.Domain.Accounts;
using TransferOrchestration.AccountBalance.Infrastructure.Persistence;
using TransferOrchestration.TransferManagement;
using TransferOrchestration.TransferManagement.Application.BalanceReservation;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Domain.Transfers;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;

namespace TransferOrchestration.IntegrationTests;

[Collection("PostgreSQL account reservation")]
public sealed class AccountReservationContractTests : IAsyncLifetime
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

        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AccountBalanceDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>().Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ConcurrentReservationsThatDoNotBothFitProduceOneBusinessLoser()
    {
        var accountId = await SeedAccountAsync(1_000m);
        var requests = new[]
        {
            Request(Guid.NewGuid(), accountId, 750m),
            Request(Guid.NewGuid(), accountId, 600m)
        };

        var results = await ReserveConcurrentlyAsync(requests);

        Assert.Equal(1, results.Count(result => result.Outcome == ReserveFundsOutcome.Succeeded));
        Assert.Equal(1, results.Count(result => result.Outcome == ReserveFundsOutcome.InsufficientBalance));
        var snapshot = await SnapshotAsync(accountId);
        Assert.True(snapshot.Available is 250m or 400m);
        Assert.True(snapshot.Reserved is 750m or 600m);
        Assert.Equal(1, snapshot.Version);
        Assert.Single(snapshot.Reservations);
        Assert.True(snapshot.Available >= 0);
        Console.WriteLine(
            $"TASK-07 critical concurrency: winners=1, businessLosers=1, " +
            $"available={snapshot.Available:0.0000}, reserved={snapshot.Reserved:0.0000}, " +
            $"version={snapshot.Version}, reservations={snapshot.Reservations.Count}; " +
            $"row={snapshot.Reservations.Single().TransferId}/{snapshot.Reservations.Single().Amount:0.0000}");
    }

    [Fact]
    public async Task ConcurrentReservationsThatStillFitBothSucceedAfterReload()
    {
        var accountId = await SeedAccountAsync(1_000m);
        var results = await ReserveConcurrentlyAsync(
            Request(Guid.NewGuid(), accountId, 100m),
            Request(Guid.NewGuid(), accountId, 200m));

        Assert.All(results, result => Assert.Equal(ReserveFundsOutcome.Succeeded, result.Outcome));
        var snapshot = await SnapshotAsync(accountId);
        Assert.Equal(700m, snapshot.Available);
        Assert.Equal(300m, snapshot.Reserved);
        Assert.Equal(2, snapshot.Version);
        Assert.Equal(2, snapshot.Reservations.Count);
    }

    [Fact]
    public async Task SameScopedContractMatchesTrackedReservationsByTransferId()
    {
        var accountId = await SeedAccountAsync(500m);
        var transferA = Guid.NewGuid();
        var transferB = Guid.NewGuid();

        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var reservations = scope.ServiceProvider.GetRequiredService<IAccountBalanceReservations>();

        Assert.Equal(ReserveFundsOutcome.Succeeded,
            (await reservations.ReserveAsync(
                Request(transferA, accountId, 100m), CancellationToken.None)).Outcome);
        Assert.Equal(ReserveFundsOutcome.Succeeded,
            (await reservations.ReserveAsync(
                Request(transferB, accountId, 100m), CancellationToken.None)).Outcome);
        Assert.Equal(ReserveFundsOutcome.AlreadyReserved,
            (await reservations.ReserveAsync(
                Request(transferA, accountId, 100m), CancellationToken.None)).Outcome);

        var snapshot = await SnapshotAsync(accountId);
        Assert.Equal((300m, 200m, 2L),
            (snapshot.Available, snapshot.Reserved, snapshot.Version));
        Assert.Equal(2, snapshot.Reservations.Count);
        Assert.Single(snapshot.Reservations, reservation => reservation.TransferId == transferA);
        Assert.Single(snapshot.Reservations, reservation => reservation.TransferId == transferB);
    }

    [Fact]
    public async Task ConcurrentEquivalentTransferIdCreatesExactlyOneFinancialHold()
    {
        var accountId = await SeedAccountAsync(500m);
        var transferId = Guid.NewGuid();

        var results = await ReserveConcurrentlyAsync(
            Request(transferId, accountId, 100m),
            Request(transferId, accountId, 100m));

        Assert.Equal(1, results.Count(result => result.Outcome == ReserveFundsOutcome.Succeeded));
        Assert.Equal(1, results.Count(result => result.Outcome == ReserveFundsOutcome.AlreadyReserved));
        var snapshot = await SnapshotAsync(accountId);
        Assert.Equal((400m, 100m, 1L),
            (snapshot.Available, snapshot.Reserved, snapshot.Version));
        var reservation = Assert.Single(snapshot.Reservations);
        Assert.Equal(transferId, reservation.TransferId);
        Assert.Equal(100m, reservation.Amount);
        Console.WriteLine(
            "TASK-07 duplicate concurrency: succeeded=1, alreadyReserved=1, " +
            "available=400.0000, reserved=100.0000, version=1, reservations=1.");
    }

    [Fact]
    public async Task ConcurrentSameTransferForDifferentAccountsClassifiesUniqueConstraintRace()
    {
        var firstAccountId = await SeedAccountAsync(500m);
        var secondAccountId = await SeedAccountAsync(500m);
        var transferId = Guid.NewGuid();

        var results = await ReserveConcurrentlyAsync(
            Request(transferId, firstAccountId, 100m),
            Request(transferId, secondAccountId, 100m));

        Assert.Equal(1, results.Count(result => result.Outcome == ReserveFundsOutcome.Succeeded));
        Assert.Equal(1, results.Count(result => result.Outcome == ReserveFundsOutcome.ConflictingReservation));
        var first = await SnapshotAsync(firstAccountId);
        var second = await SnapshotAsync(secondAccountId);
        var changed = new[] { first, second }.Single(snapshot => snapshot.Reservations.Count == 1);
        var unchanged = new[] { first, second }.Single(snapshot => snapshot.Reservations.Count == 0);
        Assert.Equal((400m, 100m, 1L),
            (changed.Available, changed.Reserved, changed.Version));
        Assert.Equal((500m, 0m, 0L),
            (unchanged.Available, unchanged.Reserved, unchanged.Version));
        Assert.Equal(transferId, Assert.Single(changed.Reservations).TransferId);
        Assert.Equal(1, await ReservationCountAsync(transferId));
        Console.WriteLine(
            "TASK-07 unique race: succeeded=1, conflictingReservation=1, " +
            "winner=400.0000/100.0000/v1, loser=500.0000/0.0000/v0, reservations=1.");
    }

    [Fact]
    public async Task EquivalentRequestIsIdempotentAndChangedIntentConflicts()
    {
        var accountId = await SeedAccountAsync(500m);
        var transferId = Guid.NewGuid();
        Assert.Equal(ReserveFundsOutcome.Succeeded,
            (await ReserveAsync(Request(transferId, accountId, 100m))).Outcome);
        Assert.Equal(ReserveFundsOutcome.AlreadyReserved,
            (await ReserveAsync(Request(transferId, accountId, 100m))).Outcome);
        Assert.Equal(ReserveFundsOutcome.ConflictingReservation,
            (await ReserveAsync(Request(transferId, accountId, 101m))).Outcome);

        var otherAccount = await SeedAccountAsync(500m);
        Assert.Equal(ReserveFundsOutcome.ConflictingReservation,
            (await ReserveAsync(Request(transferId, otherAccount, 100m))).Outcome);
        var first = await SnapshotAsync(accountId);
        var second = await SnapshotAsync(otherAccount);
        Assert.Equal((400m, 100m, 1), (first.Available, first.Reserved, first.Reservations.Count));
        Assert.Equal((500m, 0m, 0), (second.Available, second.Reserved, second.Reservations.Count));
    }

    [Fact]
    public async Task ActiveReplayRequiresCanonicalAccountCurrency()
    {
        var accountId = await SeedAccountAsync(500m);
        var transferId = Guid.NewGuid();
        Assert.Equal(ReserveFundsOutcome.Succeeded,
            (await ReserveAsync(Request(transferId, accountId, 100m))).Outcome);

        var mismatch = await ReserveAsync(new ReserveFundsRequest(transferId, accountId, 100m, "USD"));
        Assert.Equal(ReserveFundsOutcome.CurrencyMismatch, mismatch.Outcome);
        Assert.False(mismatch.IsSuccess);

        var replay = await ReserveAsync(Request(transferId, accountId, 100m));
        Assert.Equal(ReserveFundsOutcome.AlreadyReserved, replay.Outcome);
        var snapshot = await SnapshotAsync(accountId);
        Assert.Equal((400m, 100m, 1L, 1),
            (snapshot.Available, snapshot.Reserved, snapshot.Version, snapshot.Reservations.Count));
    }

    [Fact]
    public async Task ProcessStepCannotAdvanceWhenReservationContractReportsCurrencyMismatch()
    {
        var accountId = await SeedAccountAsync(500m);
        var transferId = await SeedPendingTransferAsync(accountId, 100m, "USD");
        Assert.Equal(ReserveBalanceStepOutcome.TransferRejected, await ExecuteStepAsync(transferId));
        Assert.NotEqual(TransferState.BalanceReserved, (await WorkflowSnapshotAsync(transferId)).TransferState);
        Assert.Empty((await SnapshotAsync(accountId)).Reservations);
    }

    [Fact]
    public async Task UniqueConflictClassifierNeverAcceptsCurrencyMismatch()
    {
        var accountId = await SeedAccountAsync(500m);
        var transferId = Guid.NewGuid();
        await using var provider = CreateProvider(
            new InsertReservationAndChangeCurrencyObserver(_connectionString, accountId, transferId));
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<IAccountBalanceReservations>()
            .ReserveAsync(Request(transferId, accountId, 100m), CancellationToken.None);

        Assert.Equal(ReserveFundsOutcome.CurrencyMismatch, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Equal(1, await ReservationCountAsync(transferId));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(2)]
    public async Task TerminalReservationIsNotIdempotentSuccessAndCannotAdvanceTransfer(
        int terminalStatusValue)
    {
        var terminalStatus = (BalanceReservationStatus)terminalStatusValue;
        var accountId = await SeedAccountAsync(500m);
        var transferId = await SeedPendingTransferAsync(accountId, 100m);
        Assert.Equal(ReserveFundsOutcome.Succeeded,
            (await ReserveAsync(Request(transferId.Value, accountId, 100m))).Outcome);
        await FinaliseReservationAsync(accountId, transferId.Value, terminalStatus);

        var beforeRetry = await SnapshotAsync(accountId);
        var result = await ReserveAsync(Request(transferId.Value, accountId, 100m));
        Assert.Equal(ReserveFundsOutcome.ConflictingReservation, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Equal(ReserveBalanceStepOutcome.TransferRejected, await ExecuteStepAsync(transferId));

        var afterRetry = await SnapshotAsync(accountId);
        Assert.Equal(
            (beforeRetry.Available, beforeRetry.Reserved, beforeRetry.Version, beforeRetry.Reservations.Count),
            (afterRetry.Available, afterRetry.Reserved, afterRetry.Version, afterRetry.Reservations.Count));
        Assert.Single(afterRetry.Reservations);
        Assert.NotEqual(TransferState.BalanceReserved, (await WorkflowSnapshotAsync(transferId)).TransferState);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(2)]
    public async Task UniqueConflictClassifierRejectsPersistedTerminalReservation(
        int terminalStatusValue)
    {
        var terminalStatus = (BalanceReservationStatus)terminalStatusValue;
        var accountId = await SeedAccountAsync(500m);
        var transferId = Guid.NewGuid();
        await using var provider = CreateProvider(
            new InsertTerminalReservationObserver(_connectionString, accountId, transferId, terminalStatus));
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<IAccountBalanceReservations>()
            .ReserveAsync(Request(transferId, accountId, 100m), CancellationToken.None);

        Assert.Equal(ReserveFundsOutcome.ConflictingReservation, result.Outcome);
        Assert.Equal((500m, 0m, 0L),
            ((await SnapshotAsync(accountId)).Available,
             (await SnapshotAsync(accountId)).Reserved,
             (await SnapshotAsync(accountId)).Version));
        Assert.Equal(1, await ReservationCountAsync(transferId));
    }

    [Fact]
    public async Task FailedSaveDiscardsTrackedFinancialStateBeforeSameScopeRetry()
    {
        var accountId = await SeedAccountAsync(500m);
        var transferId = Guid.NewGuid();
        var observer = new FailFirstAttemptObserver();
        await using var provider = CreateProvider(observer);
        await using var scope = provider.CreateAsyncScope();
        var reservations = scope.ServiceProvider.GetRequiredService<IAccountBalanceReservations>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => reservations.ReserveAsync(
            Request(transferId, accountId, 100m), CancellationToken.None));
        var afterFailure = await SnapshotAsync(accountId);
        Assert.Equal((500m, 0m, 0L, 0),
            (afterFailure.Available, afterFailure.Reserved, afterFailure.Version, afterFailure.Reservations.Count));

        var retry = await reservations.ReserveAsync(Request(transferId, accountId, 100m), CancellationToken.None);
        Assert.Equal(ReserveFundsOutcome.Succeeded, retry.Outcome);
        var afterRetry = await SnapshotAsync(accountId);
        Assert.Equal((400m, 100m, 1L, 1),
            (afterRetry.Available, afterRetry.Reserved, afterRetry.Version, afterRetry.Reservations.Count));
    }

    [Theory]
    [InlineData(2, "GBP", 50, ReserveFundsOutcome.AccountInactive)]
    [InlineData(1, "USD", 50, ReserveFundsOutcome.CurrencyMismatch)]
    [InlineData(1, "GBP", 501, ReserveFundsOutcome.InsufficientBalance)]
    public async Task DefinitiveAccountFailuresHaveNoFinancialEffect(
        int status,
        string currency,
        decimal amount,
        ReserveFundsOutcome expected)
    {
        var accountId = await SeedAccountAsync(500m, currency, (AccountStatus)status);
        var result = await ReserveAsync(Request(Guid.NewGuid(), accountId, amount));
        Assert.Equal(expected, result.Outcome);
        var snapshot = await SnapshotAsync(accountId);
        Assert.Equal((500m, 0m, 0L, 0),
            (snapshot.Available, snapshot.Reserved, snapshot.Version, snapshot.Reservations.Count));
    }

    [Fact]
    public async Task MissingAccountAndInvalidAmountAreDeterministicResults()
    {
        Assert.Equal(ReserveFundsOutcome.AccountNotFound,
            (await ReserveAsync(Request(Guid.NewGuid(), Guid.NewGuid(), 1m))).Outcome);
        Assert.Equal(ReserveFundsOutcome.InvalidAmount,
            (await ReserveAsync(Request(Guid.NewGuid(), Guid.NewGuid(), 1.00001m))).Outcome);
    }

    [Fact]
    public async Task ProcessStepAdvancesTransferAndProcessExactlyOnce()
    {
        var accountId = await SeedAccountAsync(500m);
        var transferId = await SeedPendingTransferAsync(accountId, 100m);

        Assert.Equal(ReserveBalanceStepOutcome.BalanceReserved, await ExecuteStepAsync(transferId));
        Assert.Equal(ReserveBalanceStepOutcome.AlreadyCompleted, await ExecuteStepAsync(transferId));

        var workflow = await WorkflowSnapshotAsync(transferId);
        Assert.Equal(TransferState.BalanceReserved, workflow.TransferState);
        Assert.Equal(TransferProcessAction.None, workflow.NextAction);
        Assert.Equal(6, workflow.TransferVersion);
        var account = await SnapshotAsync(accountId);
        Assert.Equal((400m, 100m, 1), (account.Available, account.Reserved, account.Reservations.Count));
    }

    [Fact]
    public async Task NewScopeRecoversCrashAfterAccountCommitBeforeTransferCommit()
    {
        var accountId = await SeedAccountAsync(500m);
        var transferId = await SeedPendingTransferAsync(accountId, 100m);

        Assert.Equal(ReserveFundsOutcome.Succeeded,
            (await ReserveAsync(Request(transferId.Value, accountId, 100m))).Outcome);
        Assert.Equal(TransferState.PendingBalanceReservation,
            (await WorkflowSnapshotAsync(transferId)).TransferState);

        // ExecuteStepAsync creates a completely new provider scope. Its contract call
        // observes the committed equivalent reservation and safely completes the workflow.
        Assert.Equal(ReserveBalanceStepOutcome.BalanceReserved, await ExecuteStepAsync(transferId));
        var workflow = await WorkflowSnapshotAsync(transferId);
        var account = await SnapshotAsync(accountId);
        Assert.Equal(TransferState.BalanceReserved, workflow.TransferState);
        Assert.Equal(TransferProcessAction.None, workflow.NextAction);
        Assert.Equal((400m, 100m, 1), (account.Available, account.Reserved, account.Reservations.Count));
        Console.WriteLine("TASK-07 crash recovery: account commit survived disposed scope; retry returned equivalent reservation; transfer=BalanceReserved; reservations=1.");
    }

    [Fact]
    public async Task ProductionDispatcherExecutesPersistedReserveBalanceAndRestartRecoversDueWork()
    {
        var firstAccount = await SeedAccountAsync(500m);
        var firstTransfer = await SeedPendingTransferAsync(firstAccount, 100m);
        await using (var provider = CreateProvider())
        await using (var scope = provider.CreateAsyncScope())
        {
            Assert.Equal(1, await scope.ServiceProvider
                .GetRequiredService<ITransferProcessDueWorkDispatcher>()
                .DispatchDueAsync(CancellationToken.None));
        }

        await AssertDispatchedAsync(firstAccount, firstTransfer);

        var recoveredAccount = await SeedAccountAsync(500m);
        var recoveredTransfer = await SeedPendingTransferAsync(recoveredAccount, 100m);
        // The provider/scope that persisted the work has already been disposed by
        // SeedPendingTransferAsync. A newly constructed production dispatcher must
        // rediscover the process state from PostgreSQL.
        await using (var restartedProvider = CreateProvider())
        await using (var restartedScope = restartedProvider.CreateAsyncScope())
        {
            Assert.Equal(1, await restartedScope.ServiceProvider
                .GetRequiredService<ITransferProcessDueWorkDispatcher>()
                .DispatchDueAsync(CancellationToken.None));
        }

        await AssertDispatchedAsync(recoveredAccount, recoveredTransfer);
    }

    [Fact]
    public async Task DispatcherLeavesDueContinueWorkflowUntouched()
    {
        var accountId = await SeedAccountAsync(500m);
        var transferId = await SeedContinueWorkflowAsync(accountId);
        var before = await ProcessSnapshotAsync(transferId);

        Assert.Equal(0, await DispatchAsync());

        Assert.Equal(before, await ProcessSnapshotAsync(transferId));
        Assert.Empty((await SnapshotAsync(accountId)).Reservations);
    }

    [Fact]
    public async Task DispatcherMixedBatchProcessesOnlyReserveBalance()
    {
        var unsupportedAccount = await SeedAccountAsync(500m);
        var unsupportedTransfer = await SeedContinueWorkflowAsync(unsupportedAccount);
        var before = await ProcessSnapshotAsync(unsupportedTransfer);
        var reservableAccount = await SeedAccountAsync(500m);
        var reservableTransfer = await SeedPendingTransferAsync(reservableAccount, 100m);

        Assert.Equal(1, await DispatchAsync());

        Assert.Equal(before, await ProcessSnapshotAsync(unsupportedTransfer));
        Assert.Empty((await SnapshotAsync(unsupportedAccount)).Reservations);
        await AssertDispatchedAsync(reservableAccount, reservableTransfer);
    }

    [Fact]
    public async Task CancellationDuringContentionBackoffStopsBeforeAnotherAttempt()
    {
        var accountId = await SeedAccountAsync(500m);
        var observer = new ForceConcurrencyConflictObserver(_connectionString, accountId);
        using var delay = new CancellingRetryDelay();
        await using var provider = CreateProvider(observer, delay);
        await using var scope = provider.CreateAsyncScope();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.ServiceProvider
            .GetRequiredService<IAccountBalanceReservations>()
            .ReserveAsync(Request(Guid.NewGuid(), accountId, 100m), delay.Token));

        Assert.Equal(1, observer.Attempts);
        Assert.Equal(1, delay.Delays);
        Assert.Empty((await SnapshotAsync(accountId)).Reservations);
    }

    [Fact]
    public async Task DispatcherStopsAfterDurableContentionBudgetIsExhausted()
    {
        var accountId = await SeedAccountAsync(500m);
        var transferId = await SeedPendingTransferAsync(accountId, 100m);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow.AddMinutes(1));
        var step = new SequencedReserveBalanceStep(
            transferId => ExecuteStepAsync(transferId, clock),
            ReserveBalanceStepOutcome.RetryableContention,
            ReserveBalanceStepOutcome.RetryableContention,
            ReserveBalanceStepOutcome.RetryableContention);
        await using var provider = CreateProvider(processStep: step, timeProvider: clock);

        Assert.Equal(1, await DispatchAsync(provider));
        var firstRetry = await ProcessSnapshotAsync(transferId);
        Assert.Equal(1, firstRetry.AttemptCount);
        Assert.Equal(TransferProcessStatus.Active, firstRetry.Status);
        Assert.Equal(TransferProcessAction.ReserveBalance, firstRetry.NextAction);
        Assert.True(firstRetry.NextAttemptAtUtc > clock.GetUtcNow());
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, await DispatchAsync(provider));
        var secondRetry = await ProcessSnapshotAsync(transferId);
        Assert.Equal(2, secondRetry.AttemptCount);
        Assert.Equal(TransferProcessAction.ReserveBalance, secondRetry.NextAction);
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, await DispatchAsync(provider));

        var exhausted = await ProcessSnapshotAsync(transferId);
        Assert.Equal(TransferProcessStatus.Waiting, exhausted.Status);
        Assert.Equal(TransferProcessAction.None, exhausted.NextAction);
        Assert.Null(exhausted.NextAttemptAtUtc);
        Assert.Equal(2, exhausted.AttemptCount);
        Assert.Equal(TransferState.PendingBalanceReservation, (await WorkflowSnapshotAsync(transferId)).TransferState);
        Assert.Equal(0, await DispatchAsync(provider));
        Assert.Equal(3, step.Invocations);
        Assert.Equal(exhausted, await ProcessSnapshotAsync(transferId));
    }

    [Fact]
    public async Task DispatcherPreservesContentionBudgetAcrossProviderRestart()
    {
        var accountId = await SeedAccountAsync(500m);
        var transferId = await SeedPendingTransferAsync(accountId, 100m);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow.AddMinutes(1));

        await using (var firstProvider = CreateProvider(
            processStep: new SequencedReserveBalanceStep(
                ExecuteStepAsync,
                ReserveBalanceStepOutcome.RetryableContention),
            timeProvider: clock))
        {
            Assert.Equal(1, await DispatchAsync(firstProvider));
        }

        Assert.Equal(1, (await ProcessSnapshotAsync(transferId)).AttemptCount);
        clock.Advance(TimeSpan.FromSeconds(2));
        var restartedStep = new SequencedReserveBalanceStep(
            ExecuteStepAsync,
            ReserveBalanceStepOutcome.RetryableContention,
            ReserveBalanceStepOutcome.RetryableContention);
        await using var restartedProvider = CreateProvider(processStep: restartedStep, timeProvider: clock);
        Assert.Equal(1, await DispatchAsync(restartedProvider));
        Assert.Equal(2, (await ProcessSnapshotAsync(transferId)).AttemptCount);
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, await DispatchAsync(restartedProvider));
        Assert.Equal(TransferProcessStatus.Waiting, (await ProcessSnapshotAsync(transferId)).Status);
        Assert.Equal(2, restartedStep.Invocations);
    }

    [Fact]
    public async Task DispatcherSuccessBeforeContentionExhaustionSchedulesNoFurtherRetry()
    {
        var accountId = await SeedAccountAsync(500m);
        var transferId = await SeedPendingTransferAsync(accountId, 100m);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow.AddMinutes(1));
        var step = new SequencedReserveBalanceStep(
            transferId => ExecuteStepAsync(transferId, clock),
            ReserveBalanceStepOutcome.RetryableContention,
            ReserveBalanceStepOutcome.BalanceReserved);
        await using var provider = CreateProvider(processStep: step, timeProvider: clock);

        Assert.Equal(1, await DispatchAsync(provider));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, await DispatchAsync(provider));
        Assert.Equal(0, await DispatchAsync(provider));
        Assert.Equal(2, step.Invocations);

        var process = await ProcessSnapshotAsync(transferId);
        Assert.Equal(TransferProcessStatus.Waiting, process.Status);
        Assert.Equal(TransferProcessAction.None, process.NextAction);
        Assert.Null(process.NextAttemptAtUtc);
        Assert.Equal(TransferState.BalanceReserved, (await WorkflowSnapshotAsync(transferId)).TransferState);
    }

    [Fact]
    public async Task TwoDispatchersAllowOnlyTheClaimOwnerToReserve()
    {
        var accountId = await SeedAccountAsync(500m);
        var transferId = await SeedPendingTransferAsync(accountId, 100m);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow.AddMinutes(1));
        var gate = new ClaimedExecutionGate((id, version) => ExecuteClaimedStepAsync(id, version, clock));
        await using var providerA = CreateProvider(processStep: gate, timeProvider: clock);
        await using var providerB = CreateProvider(processStep: gate, timeProvider: clock);

        var dispatchA = DispatchAsync(providerA);
        await gate.Claimed;
        Assert.Equal(0, await DispatchAsync(providerB));
        gate.Release();
        Assert.Equal(1, await dispatchA);

        Assert.Equal(1, gate.Invocations);
        Assert.Equal((400m, 100m, 1L, 1), SnapshotTuple(await SnapshotAsync(accountId)));
        var workflow = await WorkflowSnapshotAsync(transferId);
        Assert.Equal(TransferState.BalanceReserved, workflow.TransferState);
        var process = await ProcessSnapshotAsync(transferId);
        Assert.Equal(TransferProcessStatus.Waiting, process.Status);
        Assert.Equal(TransferProcessAction.None, process.NextAction);
    }

    [Fact]
    public async Task CompetingContentionWorkerCannotParkSuccessfulClaimOwnersProcess()
    {
        var accountId = await SeedAccountAsync(500m);
        var transferId = await SeedPendingTransferAsync(accountId, 100m);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow.AddMinutes(1));
        var successful = new ClaimedExecutionGate((id, version) => ExecuteClaimedStepAsync(id, version, clock));
        var contention = new SequencedReserveBalanceStep(null, ReserveBalanceStepOutcome.RetryableContention);
        await using var successfulProvider = CreateProvider(processStep: successful, timeProvider: clock);
        await using var contentionProvider = CreateProvider(processStep: contention, timeProvider: clock);

        var successDispatch = DispatchAsync(successfulProvider);
        await successful.Claimed;
        Assert.Equal(0, await DispatchAsync(contentionProvider));
        successful.Release();
        await successDispatch;

        Assert.Equal(0, contention.Invocations);
        Assert.Equal(1, await ReservationCountAsync(transferId.Value));
        Assert.Equal(TransferState.BalanceReserved, (await WorkflowSnapshotAsync(transferId)).TransferState);
        Assert.Equal(TransferProcessStatus.Waiting, (await ProcessSnapshotAsync(transferId)).Status);
    }

    [Fact]
    public async Task ExpiredClaimIsRecoveredAfterWorkerCrash()
    {
        var accountId = await SeedAccountAsync(500m);
        var transferId = await SeedPendingTransferAsync(accountId, 100m);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow.AddMinutes(1));
        await using (var claimingProvider = CreateProvider(timeProvider: clock))
        await using (var scope = claimingProvider.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<ITransferProcessManager>();
            var candidate = Assert.Single(await manager.GetDueForActionAsync(
                TransferProcessAction.ReserveBalance,
                clock.GetUtcNow(),
                1,
                CancellationToken.None));
            var claim = await manager.TryClaimDueAsync(
                transferId,
                TransferProcessAction.ReserveBalance,
                candidate.Version,
                clock.GetUtcNow(),
                clock.GetUtcNow() + TransferProcessDueWorkDispatcher.ClaimLease,
                CancellationToken.None);
            Assert.NotNull(claim);
        }

        await using (var beforeExpiry = CreateProvider(timeProvider: clock))
        {
            Assert.Equal(0, await DispatchAsync(beforeExpiry));
        }

        clock.Advance(TransferProcessDueWorkDispatcher.ClaimLease + TimeSpan.FromSeconds(1));
        await using var recoveredProvider = CreateProvider(timeProvider: clock);
        Assert.Equal(1, await DispatchAsync(recoveredProvider));
        Assert.Equal(TransferState.BalanceReserved, (await WorkflowSnapshotAsync(transferId)).TransferState);
        Assert.Equal(1, await ReservationCountAsync(transferId.Value));
    }

    [Fact]
    public async Task StaleCandidateVersionCannotClaimANewerContentionRetry()
    {
        var accountId = await SeedAccountAsync(500m);
        var transferId = await SeedPendingTransferAsync(accountId, 100m);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow.AddMinutes(1));
        DueTransferProcess staleCandidate;

        await using (var provider = CreateProvider(timeProvider: clock))
        await using (var scope = provider.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<ITransferProcessManager>();
            staleCandidate = Assert.Single(await manager.GetDueForActionAsync(
                TransferProcessAction.ReserveBalance, clock.GetUtcNow(), 1, CancellationToken.None));
            var claim = await manager.TryClaimDueAsync(
                transferId, TransferProcessAction.ReserveBalance, staleCandidate.Version,
                clock.GetUtcNow(), clock.GetUtcNow() + TransferProcessDueWorkDispatcher.ClaimLease,
                CancellationToken.None);
            Assert.NotNull(claim);
            await manager.RecordAttemptAsync(
                transferId, claim.ClaimedVersion, clock.GetUtcNow().AddSeconds(1),
                clock.GetUtcNow(), CancellationToken.None);
        }

        clock.Advance(TimeSpan.FromSeconds(2));
        await using var retryProvider = CreateProvider(timeProvider: clock);
        await using var retryScope = retryProvider.CreateAsyncScope();
        var retryManager = retryScope.ServiceProvider.GetRequiredService<ITransferProcessManager>();
        Assert.Null(await retryManager.TryClaimDueAsync(
            transferId, TransferProcessAction.ReserveBalance, staleCandidate.Version,
            clock.GetUtcNow(), clock.GetUtcNow() + TransferProcessDueWorkDispatcher.ClaimLease,
            CancellationToken.None));
        Assert.Equal(1, (await ProcessSnapshotAsync(transferId)).AttemptCount);

        var freshCandidate = Assert.Single(await retryManager.GetDueForActionAsync(
            TransferProcessAction.ReserveBalance, clock.GetUtcNow(), 1, CancellationToken.None));
        var freshClaim = await retryManager.TryClaimDueAsync(
            transferId, TransferProcessAction.ReserveBalance, freshCandidate.Version,
            clock.GetUtcNow(), clock.GetUtcNow() + TransferProcessDueWorkDispatcher.ClaimLease,
            CancellationToken.None);
        Assert.NotNull(freshClaim);
        Assert.Equal(1, freshClaim.AttemptCount);
    }

    [Fact]
    public async Task ManyStaleDispatchersCannotBypassDurableContentionBudget()
    {
        var accountId = await SeedAccountAsync(500m);
        var transferId = await SeedPendingTransferAsync(accountId, 100m);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow.AddMinutes(1));
        DueTransferProcess candidate;
        await using (var discoveryProvider = CreateProvider(timeProvider: clock))
        await using (var discoveryScope = discoveryProvider.CreateAsyncScope())
        {
            candidate = Assert.Single(await discoveryScope.ServiceProvider
                .GetRequiredService<ITransferProcessManager>()
                .GetDueForActionAsync(TransferProcessAction.ReserveBalance, clock.GetUtcNow(), 1, CancellationToken.None));
        }
        var staleCandidates = Enumerable.Repeat(candidate, 8).ToArray();
        var step = new SequencedReserveBalanceStep(
            null,
            ReserveBalanceStepOutcome.RetryableContention,
            ReserveBalanceStepOutcome.RetryableContention,
            ReserveBalanceStepOutcome.RetryableContention);
        await using var dispatcherProvider = CreateProvider(processStep: step, timeProvider: clock);

        for (var execution = 0; execution < 3; execution++)
        {
            Assert.Equal(1, await DispatchAsync(dispatcherProvider));
            clock.Advance(TimeSpan.FromSeconds(2));
            foreach (var stale in staleCandidates)
            {
                await using var staleScope = dispatcherProvider.CreateAsyncScope();
                var staleManager = staleScope.ServiceProvider.GetRequiredService<ITransferProcessManager>();
                Assert.Null(await staleManager.TryClaimDueAsync(
                    transferId, TransferProcessAction.ReserveBalance, stale.Version,
                    clock.GetUtcNow(), clock.GetUtcNow() + TransferProcessDueWorkDispatcher.ClaimLease,
                    CancellationToken.None));
            }
        }

        Assert.Equal(0, await DispatchAsync(dispatcherProvider));
        Assert.Equal(3, step.Invocations);
        var process = await ProcessSnapshotAsync(transferId);
        Assert.Equal(2, process.AttemptCount);
        Assert.Equal(TransferProcessStatus.Waiting, process.Status);
        Assert.Equal(TransferProcessAction.None, process.NextAction);
    }

    [Fact]
    public async Task ExpiredOwnerFinancialCommitRearmsProcessParkedByNewOwner()
    {
        var accountId = await SeedAccountAsync(500m);
        var transferId = await SeedPendingTransferAsync(accountId, 100m);
        await SetProcessAttemptCountAsync(transferId, 2);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow.AddMinutes(1));
        var gate = new BlockingReservationCommitObserver();
        await using var staleOwner = CreateProvider(observer: gate, timeProvider: clock);
        var staleDispatch = DispatchAsync(staleOwner);
        await gate.Entered;

        clock.Advance(TransferProcessDueWorkDispatcher.ClaimLease + TimeSpan.FromSeconds(1));
        var contention = new SequencedReserveBalanceStep(null, ReserveBalanceStepOutcome.RetryableContention);
        await using (var newOwner = CreateProvider(processStep: contention, timeProvider: clock))
        {
            Assert.Equal(1, await DispatchAsync(newOwner));
        }
        Assert.Equal(TransferProcessStatus.Waiting, (await ProcessSnapshotAsync(transferId)).Status);

        gate.Release();
        Assert.Equal(1, await staleDispatch);
        Assert.Equal((400m, 100m, 1L, 1), SnapshotTuple(await SnapshotAsync(accountId)));
        var recovered = await ProcessSnapshotAsync(transferId);
        Assert.Equal(TransferProcessStatus.Active, recovered.Status);
        Assert.Equal(TransferProcessAction.ReserveBalance, recovered.NextAction);

        await using var recoveryProvider = CreateProvider(timeProvider: clock);
        Assert.Equal(1, await DispatchAsync(recoveryProvider));
        Assert.Equal(TransferState.BalanceReserved, (await WorkflowSnapshotAsync(transferId)).TransferState);
        Assert.Equal(1, await ReservationCountAsync(transferId.Value));
    }

    [Fact]
    public async Task NewOwnerMayCompleteBeforeExpiredOwnerResumes()
    {
        var accountId = await SeedAccountAsync(500m);
        var transferId = await SeedPendingTransferAsync(accountId, 100m);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow.AddMinutes(1));
        var gate = new BlockingReservationCommitObserver();
        await using var staleOwner = CreateProvider(observer: gate, timeProvider: clock);
        var staleDispatch = DispatchAsync(staleOwner);
        await gate.Entered;

        clock.Advance(TransferProcessDueWorkDispatcher.ClaimLease + TimeSpan.FromSeconds(1));
        await using (var newOwner = CreateProvider(timeProvider: clock))
        {
            Assert.Equal(1, await DispatchAsync(newOwner));
        }

        gate.Release();
        Assert.Equal(1, await staleDispatch);
        Assert.Equal((400m, 100m, 1L, 1), SnapshotTuple(await SnapshotAsync(accountId)));
        Assert.Equal(TransferState.BalanceReserved, (await WorkflowSnapshotAsync(transferId)).TransferState);
        Assert.Equal(TransferProcessStatus.Waiting, (await ProcessSnapshotAsync(transferId)).Status);
    }

    private async Task<IReadOnlyList<ReserveFundsResult>> ReserveConcurrentlyAsync(params ReserveFundsRequest[] requests)
    {
        await using var provider = CreateProvider(new TwoPartyLoadGate());
        var tasks = requests.Select(async request =>
        {
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IAccountBalanceReservations>()
                .ReserveAsync(request, CancellationToken.None);
        });
        return await Task.WhenAll(tasks);
    }

    private async Task<ReserveFundsResult> ReserveAsync(ReserveFundsRequest request)
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAccountBalanceReservations>()
            .ReserveAsync(request, CancellationToken.None);
    }

    private async Task<ReserveBalanceStepOutcome> ExecuteStepAsync(TransferId transferId)
        => await ExecuteStepAsync(transferId, null);

    private async Task<ReserveBalanceStepOutcome> ExecuteStepAsync(TransferId transferId, TimeProvider? timeProvider)
    {
        await using var provider = CreateProvider(timeProvider: timeProvider);
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IReserveBalanceProcessStep>()
            .ExecuteAsync(transferId, (await ProcessSnapshotAsync(transferId)).Version, CancellationToken.None);
    }

    private async Task<ReserveBalanceStepOutcome> ExecuteClaimedStepAsync(
        TransferId transferId,
        long claimedVersion,
        TimeProvider timeProvider)
    {
        await using var provider = CreateProvider(timeProvider: timeProvider);
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IReserveBalanceProcessStep>()
            .ExecuteAsync(transferId, claimedVersion, CancellationToken.None);
    }

    private static (decimal Available, decimal Reserved, long Version, int Reservations) SnapshotTuple(AccountSnapshot snapshot) =>
        (snapshot.Available, snapshot.Reserved, snapshot.Version, snapshot.Reservations.Count);

    private async Task<Guid> SeedAccountAsync(
        decimal balance,
        string currency = "GBP",
        AccountStatus status = AccountStatus.Active)
    {
        var id = Guid.NewGuid();
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AccountBalanceDbContext>();
        context.Accounts.Add(Account.Create(id, currency, balance, status));
        await context.SaveChangesAsync();
        return id;
    }

    private async Task<TransferId> SeedPendingTransferAsync(Guid sourceAccountId, decimal amount, string currency = "GBP")
    {
        var now = DateTimeOffset.UtcNow;
        var transfer = Transfer.Create(sourceAccountId, Guid.NewGuid(), amount, currency, TransferType.DomesticInterbank, now);
        transfer.Submit(now);
        transfer.RequestAuthorisation(now);
        transfer.Authorise(now);
        transfer.BeginFraudScreening(now);
        transfer.RequestBalanceReservation(now);
        var process = TransferProcessState.Create(transfer.Id, Guid.NewGuid(), now);
        process.Schedule(TransferProcessAction.ReserveBalance, now, now);

        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>();
        context.Transfers.Add(transfer);
        context.TransferProcessStates.Add(process);
        await context.SaveChangesAsync();
        return transfer.Id;
    }

    private async Task<TransferId> SeedContinueWorkflowAsync(Guid sourceAccountId)
    {
        var now = DateTimeOffset.UtcNow;
        var transfer = Transfer.Create(sourceAccountId, Guid.NewGuid(), 100m, "GBP", TransferType.DomesticInterbank, now);
        var process = TransferProcessState.Create(transfer.Id, Guid.NewGuid(), now);
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>();
        context.AddRange(transfer, process);
        await context.SaveChangesAsync();
        return transfer.Id;
    }

    private async Task<int> DispatchAsync()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ITransferProcessDueWorkDispatcher>()
            .DispatchDueAsync(CancellationToken.None);
    }

    private static async Task<int> DispatchAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ITransferProcessDueWorkDispatcher>()
            .DispatchDueAsync(CancellationToken.None);
    }

    private async Task<AccountSnapshot> SnapshotAsync(Guid accountId)
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var account = await scope.ServiceProvider.GetRequiredService<AccountBalanceDbContext>()
            .Accounts.Include(candidate => candidate.Reservations)
            .AsNoTracking().SingleAsync(candidate => candidate.Id == new AccountId(accountId));
        return new AccountSnapshot(account.AvailableBalance, account.ReservedBalance, account.Version,
            account.Reservations.Select(reservation => new ReservationRow(reservation.TransferId, reservation.Amount)).ToList());
    }

    private async Task<WorkflowSnapshot> WorkflowSnapshotAsync(TransferId transferId)
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>();
        var transfer = await context.Transfers.AsNoTracking().SingleAsync(candidate => candidate.Id == transferId);
        var process = await context.TransferProcessStates.AsNoTracking().SingleAsync(candidate => candidate.TransferId == transferId);
        return new WorkflowSnapshot(transfer.State, transfer.Version, process.NextAction);
    }

    private async Task<ProcessSnapshot> ProcessSnapshotAsync(TransferId transferId)
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var process = await scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>()
            .TransferProcessStates.AsNoTracking().SingleAsync(candidate => candidate.TransferId == transferId);
        return new ProcessSnapshot(
            process.Status,
            process.CurrentStep,
            process.NextAction,
            process.AttemptCount,
            process.NextAttemptAtUtc,
            process.Version,
            process.UpdatedAtUtc);
    }

    private async Task<int> ReservationCountAsync(Guid transferId)
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AccountBalanceDbContext>()
            .Set<BalanceReservation>()
            .AsNoTracking()
            .CountAsync(reservation => reservation.TransferId == transferId);
    }

    private async Task SetProcessAttemptCountAsync(TransferId transferId, int count)
    {
        await using var provider = CreateProvider();
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

    private async Task FinaliseReservationAsync(
        Guid accountId,
        Guid transferId,
        BalanceReservationStatus status)
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AccountBalanceDbContext>();
        var account = await context.Accounts.Include(candidate => candidate.Reservations)
            .SingleAsync(candidate => candidate.Id == new AccountId(accountId));
        if (status == BalanceReservationStatus.Released)
        {
            account.ReleaseReservation(transferId, DateTimeOffset.UtcNow);
        }
        else
        {
            account.ConsumeReservation(transferId, DateTimeOffset.UtcNow);
        }

        await context.SaveChangesAsync();
    }

    private async Task AssertDispatchedAsync(Guid accountId, TransferId transferId)
    {
        var account = await SnapshotAsync(accountId);
        var workflow = await WorkflowSnapshotAsync(transferId);
        Assert.Equal((400m, 100m, 1L, 1),
            (account.Available, account.Reserved, account.Version, account.Reservations.Count));
        Assert.Equal(TransferState.BalanceReserved, workflow.TransferState);
        Assert.Equal(TransferProcessAction.None, workflow.NextAction);
    }

    private ServiceProvider CreateProvider(
        IReservationAttemptObserver? observer = null,
        IReservationRetryDelay? retryDelay = null,
        IReserveBalanceProcessStep? processStep = null,
        TimeProvider? timeProvider = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddAccountBalanceModule(_connectionString);
        services.AddTransferManagementModule(_connectionString, new ConfigurationBuilder().Build());
        if (observer is not null)
        {
            services.Replace(ServiceDescriptor.Singleton<IReservationAttemptObserver>(observer));
        }

        if (retryDelay is not null)
        {
            services.Replace(ServiceDescriptor.Singleton<IReservationRetryDelay>(retryDelay));
        }


        if (processStep is not null)
        {
            services.Replace(ServiceDescriptor.Singleton<IReserveBalanceProcessStep>(processStep));
        }

        if (timeProvider is not null)
        {
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(timeProvider));
        }

        return services.BuildServiceProvider();
    }

    private static ReserveFundsRequest Request(Guid transferId, Guid accountId, decimal amount) =>
        new(transferId, accountId, amount, "GBP");

    private sealed record ReservationRow(Guid TransferId, decimal Amount);
    private sealed record AccountSnapshot(decimal Available, decimal Reserved, long Version, IReadOnlyList<ReservationRow> Reservations);
    private sealed record WorkflowSnapshot(TransferState TransferState, long TransferVersion, TransferProcessAction NextAction);
    private sealed record ProcessSnapshot(
        TransferProcessStatus Status,
        TransferProcessStep CurrentStep,
        TransferProcessAction NextAction,
        int AttemptCount,
        DateTimeOffset? NextAttemptAtUtc,
        long Version,
        DateTimeOffset UpdatedAtUtc);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration) => utcNow += duration;
    }

    private sealed class SequencedReserveBalanceStep(
        Func<TransferId, Task<ReserveBalanceStepOutcome>>? executeSuccess = null,
        params ReserveBalanceStepOutcome[] outcomes)
        : IReserveBalanceProcessStep
    {
        private int _invocations;

        public int Invocations => _invocations;

        public async Task<ReserveBalanceStepOutcome> ExecuteAsync(
            TransferId transferId,
            long claimedVersion,
            CancellationToken cancellationToken)
        {
            var invocation = Interlocked.Increment(ref _invocations);
            if (outcomes[invocation - 1] == ReserveBalanceStepOutcome.BalanceReserved)
            {
                return await (executeSuccess ?? throw new InvalidOperationException("A success executor is required."))(transferId);
            }

            return outcomes[invocation - 1];
        }
    }

    private sealed class ClaimedExecutionGate(Func<TransferId, long, Task<ReserveBalanceStepOutcome>> execute)
        : IReserveBalanceProcessStep
    {
        private readonly TaskCompletionSource _claimed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _invocations;

        public Task Claimed => _claimed.Task;

        public int Invocations => _invocations;

        public void Release() => _released.TrySetResult();

        public async Task<ReserveBalanceStepOutcome> ExecuteAsync(
            TransferId transferId,
            long claimedVersion,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocations);
            _claimed.TrySetResult();
            await _released.Task.WaitAsync(cancellationToken);
            return await execute(transferId, claimedVersion);
        }
    }

    private sealed class TwoPartyLoadGate : IReservationAttemptObserver
    {
        private readonly TaskCompletionSource _bothLoaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public async Task AfterAccountLoadedAsync(int attempt, CancellationToken cancellationToken)
        {
            if (attempt != 1)
            {
                return;
            }

            if (Interlocked.Increment(ref _arrivals) == 2)
            {
                _bothLoaded.TrySetResult();
            }

            await _bothLoaded.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class BlockingReservationCommitObserver : IReservationAttemptObserver
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _blocked;

        public Task Entered => _entered.Task;

        public void Release() => _released.TrySetResult();

        public async Task AfterAccountLoadedAsync(int attempt, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _blocked, 1) != 0)
            {
                return;
            }

            _entered.TrySetResult();
            await _released.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class FailFirstAttemptObserver : IReservationAttemptObserver
    {
        private int _attempts;

        public Task AfterAccountLoadedAsync(int attempt, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _attempts) == 1)
            {
                throw new InvalidOperationException("Deliberate failure before persistence.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class InsertTerminalReservationObserver(
        string connectionString,
        Guid accountId,
        Guid transferId,
        BalanceReservationStatus status) : IReservationAttemptObserver
    {
        private int _inserted;

        public async Task AfterAccountLoadedAsync(int attempt, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _inserted, 1) != 0)
            {
                return;
            }

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO account_balance.balance_reservations
                    (id, account_id, transfer_id, amount, status, created_at_utc, finalised_at_utc)
                VALUES (@id, @account, @transfer, 100, @status, @now, @now)
                """;
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("account", accountId);
            command.Parameters.AddWithValue("transfer", transferId);
            command.Parameters.AddWithValue("status", status.ToString());
            command.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private sealed class InsertReservationAndChangeCurrencyObserver(
        string connectionString,
        Guid accountId,
        Guid transferId) : IReservationAttemptObserver
    {
        private int _inserted;

        public async Task AfterAccountLoadedAsync(int attempt, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _inserted, 1) != 0)
            {
                return;
            }

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE account_balance.accounts SET currency = 'USD' WHERE id = @account;
                INSERT INTO account_balance.balance_reservations
                    (id, account_id, transfer_id, amount, status, created_at_utc, finalised_at_utc)
                VALUES (@id, @account, @transfer, 100, 'Active', @now, NULL)
                """;
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("account", accountId);
            command.Parameters.AddWithValue("transfer", transferId);
            command.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private sealed class ForceConcurrencyConflictObserver(
        string connectionString,
        Guid accountId) : IReservationAttemptObserver
    {
        public int Attempts { get; private set; }

        public async Task AfterAccountLoadedAsync(int attempt, CancellationToken cancellationToken)
        {
            Attempts++;
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE account_balance.accounts SET version = version + 1 WHERE id = @account";
            command.Parameters.AddWithValue("account", accountId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private sealed class CancellingRetryDelay : IReservationRetryDelay, IDisposable
    {
        private readonly CancellationTokenSource _source = new();

        public CancellationToken Token => _source.Token;
        public int Delays { get; private set; }

        public Task DelayAsync(int failedAttempt, CancellationToken cancellationToken)
        {
            Delays++;
            _source.Cancel();
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public void Dispose() => _source.Dispose();
    }
}

[CollectionDefinition("PostgreSQL account reservation", DisableParallelization = true)]
public sealed class PostgreSqlAccountReservationGroup;
