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
        Assert.Equal(TransferProcessAction.ContinueWorkflow, workflow.NextAction);
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
        Assert.Equal(TransferProcessAction.ContinueWorkflow, workflow.NextAction);
        Assert.Equal((400m, 100m, 1), (account.Available, account.Reserved, account.Reservations.Count));
        Console.WriteLine("TASK-07 crash recovery: account commit survived disposed scope; retry returned equivalent reservation; transfer=BalanceReserved; reservations=1.");
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
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IReserveBalanceProcessStep>()
            .ExecuteAsync(transferId, CancellationToken.None);
    }

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

    private async Task<TransferId> SeedPendingTransferAsync(Guid sourceAccountId, decimal amount)
    {
        var now = DateTimeOffset.UtcNow;
        var transfer = Transfer.Create(sourceAccountId, Guid.NewGuid(), amount, "GBP", TransferType.DomesticInterbank, now);
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

    private async Task<int> ReservationCountAsync(Guid transferId)
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AccountBalanceDbContext>()
            .Set<BalanceReservation>()
            .AsNoTracking()
            .CountAsync(reservation => reservation.TransferId == transferId);
    }

    private ServiceProvider CreateProvider(IReservationAttemptObserver? observer = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddAccountBalanceModule(_connectionString);
        services.AddTransferManagementModule(_connectionString, new ConfigurationBuilder().Build());
        if (observer is not null)
        {
            services.Replace(ServiceDescriptor.Singleton<IReservationAttemptObserver>(observer));
        }

        return services.BuildServiceProvider();
    }

    private static ReserveFundsRequest Request(Guid transferId, Guid accountId, decimal amount) =>
        new(transferId, accountId, amount, "GBP");

    private sealed record ReservationRow(Guid TransferId, decimal Amount);
    private sealed record AccountSnapshot(decimal Available, decimal Reserved, long Version, IReadOnlyList<ReservationRow> Reservations);
    private sealed record WorkflowSnapshot(TransferState TransferState, long TransferVersion, TransferProcessAction NextAction);

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
}

[CollectionDefinition("PostgreSQL account reservation", DisableParallelization = true)]
public sealed class PostgreSqlAccountReservationGroup;
