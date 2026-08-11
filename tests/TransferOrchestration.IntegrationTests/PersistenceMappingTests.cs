using Microsoft.EntityFrameworkCore;
using Npgsql;
using TransferOrchestration.BuildingBlocks.Domain;
using TransferOrchestration.AccountBalance.Application.Persistence;
using TransferOrchestration.AccountBalance.Domain.Accounts;
using TransferOrchestration.AccountBalance.Infrastructure.Persistence;
using TransferOrchestration.AccountBalance.Infrastructure.Persistence.Repositories;
using TransferOrchestration.TransferManagement.Application.Idempotency;
using TransferOrchestration.TransferManagement.Application.Persistence;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Domain.Transfers;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence.Idempotency;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence.Repositories;

namespace TransferOrchestration.IntegrationTests;

public sealed class PersistenceMappingTests : IAsyncLifetime
{
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING")
        ?? throw new InvalidOperationException(
            "Destructive PostgreSQL tests require an explicit TEST_DATABASE_CONNECTION_STRING. " +
            "No application database fallback is allowed.");

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "DROP SCHEMA IF EXISTS transfer_management CASCADE; DROP SCHEMA IF EXISTS account_balance CASCADE;";
            await command.ExecuteNonQueryAsync();
        }

        await using var transferContext = CreateTransferContext();
        await transferContext.Database.MigrateAsync();
        await using var accountContext = CreateAccountContext();
        await accountContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task MigrationsCreateBothModuleOwnedSchemasAndTables()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE (table_schema, table_name) IN (
                ('transfer_management', 'transfers'),
                ('transfer_management', 'transfer_process_states'),
                ('account_balance', 'accounts'),
                ('account_balance', 'balance_reservations'));
            """;

        Assert.Equal(4L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task TransferPersistsAndReloadsThroughRepository()
    {
        var transfer = Transfer.Create(Guid.NewGuid(), Guid.NewGuid(), 125.50m, "GBP", TransferType.InternalBank, DateTimeOffset.UtcNow);
        await using (var context = CreateTransferContext())
        {
            var repository = CreateTransferRepository(context);
            await repository.AddAsync(transfer, CancellationToken.None);
            await repository.SaveChangesAsync(CancellationToken.None);
        }

        await using var readContext = CreateTransferContext();
        var readRepository = CreateTransferRepository(readContext);
        var reloaded = await readRepository.GetByIdAsync(transfer.Id, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Equal(transfer.Id, reloaded.Id);
        Assert.Equal(125.50m, reloaded.Amount);
        Assert.Equal("GBP", reloaded.Currency);
    }

    [Fact]
    public async Task AccountWithReservationPersistsAndReloadsThroughRepository()
    {
        var account = Account.Create(Guid.NewGuid(), "GBP", 500m);
        var transferId = Guid.NewGuid();
        account.Reserve(transferId, 125m, DateTimeOffset.UtcNow);
        await using (var context = CreateAccountContext())
        {
            var repository = CreateAccountRepository(context);
            await repository.AddAsync(account, CancellationToken.None);
            await repository.SaveChangesAsync(CancellationToken.None);
        }

        await using var readContext = CreateAccountContext();
        var readRepository = CreateAccountRepository(readContext);
        var reloaded = await readRepository.GetByIdAsync(account.Id, transferId, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Equal(375m, reloaded.AvailableBalance);
        Assert.Equal(125m, reloaded.ReservedBalance);
        Assert.Equal(transferId, Assert.Single(reloaded.Reservations).TransferId);
    }

    [Fact]
    public async Task FourDecimalMonetaryValuesRoundTripExactly()
    {
        const decimal openingBalance = 999.9999m;
        const decimal reservedAmount = 123.4567m;
        const decimal transferAmount = 456.7891m;
        var transfer = Transfer.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            transferAmount,
            "GBP",
            TransferType.InternalBank,
            DateTimeOffset.UtcNow);
        var account = Account.Create(Guid.NewGuid(), "GBP", openingBalance);
        var reservationTransferId = Guid.NewGuid();
        account.Reserve(reservationTransferId, reservedAmount, DateTimeOffset.UtcNow);

        await using (var transferContext = CreateTransferContext())
        {
            transferContext.Transfers.Add(transfer);
            await transferContext.SaveChangesAsync();
        }

        await using (var accountContext = CreateAccountContext())
        {
            accountContext.Accounts.Add(account);
            await accountContext.SaveChangesAsync();
        }

        await using var readTransferContext = CreateTransferContext();
        var reloadedTransfer = await readTransferContext.Transfers.SingleAsync(
            candidate => candidate.Id == transfer.Id);
        await using var readAccountContext = CreateAccountContext();
        var reloadedAccount = await readAccountContext.Accounts
            .Include(candidate => candidate.Reservations)
            .SingleAsync(candidate => candidate.Id == account.Id);

        Assert.Equal(transferAmount, reloadedTransfer.Amount);
        Assert.Equal(openingBalance - reservedAmount, reloadedAccount.AvailableBalance);
        Assert.Equal(reservedAmount, reloadedAccount.ReservedBalance);
        Assert.Equal(reservedAmount, Assert.Single(reloadedAccount.Reservations).Amount);
    }

    [Fact]
    public async Task AccountRepositoryLoadsOnlyReservationRequestedForCurrentOperation()
    {
        var account = Account.Create(Guid.NewGuid(), "GBP", 500m);
        var requestedTransferId = Guid.NewGuid();
        var unrelatedTransferId = Guid.NewGuid();
        account.Reserve(requestedTransferId, 25m, DateTimeOffset.UtcNow);
        account.Reserve(unrelatedTransferId, 50m, DateTimeOffset.UtcNow);

        await using (var setupContext = CreateAccountContext())
        {
            setupContext.Accounts.Add(account);
            await setupContext.SaveChangesAsync();
        }

        await using var context = CreateAccountContext();
        var repository = CreateAccountRepository(context);
        var reloaded = await repository.GetByIdAsync(
            account.Id,
            requestedTransferId,
            CancellationToken.None);

        Assert.NotNull(reloaded);
        var reservation = Assert.Single(reloaded.Reservations);
        Assert.Equal(requestedTransferId, reservation.TransferId);
    }

    [Fact]
    public async Task DuplicateReservationTransferIdentifierIsRejectedByDatabase()
    {
        var account = Account.Create(Guid.NewGuid(), "GBP", 500m);
        var transferId = Guid.NewGuid();
        account.Reserve(transferId, 10m, DateTimeOffset.UtcNow);
        await using var context = CreateAccountContext();
        context.Accounts.Add(account);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO account_balance.balance_reservations
                (id, account_id, transfer_id, amount, status, created_at_utc)
            VALUES ({Guid.NewGuid()}, {account.Id.Value}, {transferId}, {10m}, {"Active"}, {DateTimeOffset.UtcNow})
            """));
    }

    [Fact]
    public async Task NegativeBalanceIsRejectedByDatabase()
    {
        var account = Account.Create(Guid.NewGuid(), "GBP", 100m);
        await using var context = CreateAccountContext();
        context.Accounts.Add(account);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE account_balance.accounts SET available_balance = {-1m} WHERE id = {account.Id.Value}"));
    }

    [Fact]
    public async Task StaleAccountRepositoryWriterGetsExplicitConflictAndCannotOverwriteWinner()
    {
        var account = Account.Create(Guid.NewGuid(), "GBP", 100m);
        await using (var setup = CreateAccountContext())
        {
            setup.Accounts.Add(account);
            await setup.SaveChangesAsync();
        }

        await using var firstContext = CreateAccountContext();
        await using var secondContext = CreateAccountContext();
        var firstRepository = CreateAccountRepository(firstContext);
        var staleRepository = CreateAccountRepository(secondContext);
        var winningTransferId = Guid.NewGuid();
        var losingTransferId = Guid.NewGuid();
        var first = await firstRepository.GetByIdAsync(account.Id, winningTransferId, CancellationToken.None);
        var stale = await staleRepository.GetByIdAsync(account.Id, losingTransferId, CancellationToken.None);
        Assert.NotNull(first);
        Assert.NotNull(stale);
        Assert.Equal(0, first.Version);
        Assert.Equal(0, stale.Version);

        Assert.True(firstContext.Model.FindEntityType(typeof(Account))!
            .FindProperty(nameof(Account.Version))!.IsConcurrencyToken);

        first.Reserve(winningTransferId, 10m, DateTimeOffset.UtcNow);
        stale.Reserve(losingTransferId, 20m, DateTimeOffset.UtcNow);
        await firstRepository.SaveChangesAsync(CancellationToken.None);
        var conflict = await Assert.ThrowsAsync<AccountConcurrencyConflictException>(
            () => staleRepository.SaveChangesAsync(CancellationToken.None));
        Assert.Equal(account.Id.Value, conflict.AccountId);

        var sameRepositoryReload = await staleRepository.GetByIdAsync(
            account.Id,
            losingTransferId,
            CancellationToken.None);
        Assert.NotNull(sameRepositoryReload);
        Assert.Equal(1, sameRepositoryReload.Version);
        Assert.Equal(90m, sameRepositoryReload.AvailableBalance);
        Assert.Equal(10m, sameRepositoryReload.ReservedBalance);
        Assert.Empty(sameRepositoryReload.Reservations);

        await using var reloadContext = CreateAccountContext();
        var reloadRepository = CreateAccountRepository(reloadContext);
        var winner = await reloadRepository.GetByIdAsync(account.Id, winningTransferId, CancellationToken.None);
        Assert.NotNull(winner);
        Assert.Equal(1, winner.Version);
        Assert.Equal(90m, winner.AvailableBalance);
        Assert.Equal(10m, winner.ReservedBalance);
        var winningReservation = Assert.Single(winner.Reservations);
        Assert.Equal(10m, winningReservation.Amount);

        Console.WriteLine(
            "Concurrency evidence: both writers loaded version=0, available=100, reserved=0; " +
            "winner committed version=1, available=90, reserved=10; stale writer conflict; " +
            "reload version=1, available=90, reserved=10.");
    }

    [Fact]
    public async Task StaleTransferRepositoryWriterGetsExplicitConflictAndCannotOverwriteWinner()
    {
        var transfer = Transfer.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            100m,
            "GBP",
            TransferType.InternalBank,
            DateTimeOffset.UtcNow);
        await using (var setup = CreateTransferContext())
        {
            setup.Transfers.Add(transfer);
            await setup.SaveChangesAsync();
        }

        await using var firstContext = CreateTransferContext();
        await using var staleContext = CreateTransferContext();
        var firstRepository = CreateTransferRepository(firstContext);
        var staleRepository = CreateTransferRepository(staleContext);
        var first = await firstRepository.GetByIdAsync(transfer.Id, CancellationToken.None);
        var stale = await staleRepository.GetByIdAsync(transfer.Id, CancellationToken.None);
        Assert.NotNull(first);
        Assert.NotNull(stale);
        Assert.Equal(0, first.Version);
        Assert.Equal(0, stale.Version);
        Assert.True(firstContext.Model.FindEntityType(typeof(Transfer))!
            .FindProperty(nameof(Transfer.Version))!.IsConcurrencyToken);

        var winnerTime = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
        first.Submit(winnerTime);
        stale.Submit(winnerTime.AddMinutes(1));
        await firstRepository.SaveChangesAsync(CancellationToken.None);

        var conflict = await Assert.ThrowsAsync<TransferConcurrencyConflictException>(
            () => staleRepository.SaveChangesAsync(CancellationToken.None));
        Assert.Equal(transfer.Id.Value, conflict.TransferId);

        await using var reloadContext = CreateTransferContext();
        var winner = await CreateTransferRepository(reloadContext)
            .GetByIdAsync(transfer.Id, CancellationToken.None);
        Assert.NotNull(winner);
        Assert.Equal(TransferState.Submitted, winner.State);
        Assert.Equal(1, winner.Version);
        Assert.Equal(winnerTime, winner.UpdatedAtUtc);
    }

    [Fact]
    public async Task SameKeyAndFingerprintSequentiallyCreatesOneProcessingRecord()
    {
        var key = $"sequential-{Guid.NewGuid():N}";
        var fingerprint = Fingerprint(100m);
        await using var context = CreateTransferContext();
        var store = new TransferSubmissionIdempotencyStore(context);

        var owner = await store.TryClaimAsync(key, fingerprint, DateTimeOffset.UtcNow, CancellationToken.None);
        var duplicate = await store.TryClaimAsync(key, fingerprint, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(IdempotencyClaimOutcome.Owner, owner.Outcome);
        Assert.NotNull(owner.OwnerToken);
        Assert.Equal(IdempotencyClaimOutcome.Processing, duplicate.Outcome);
        Assert.Equal(1, await context.IdempotencyRecords.CountAsync(record => record.Key == key));
    }

    [Fact]
    public async Task SameKeyAndDifferentFingerprintReturnsConflictWithoutOverwrite()
    {
        var key = $"conflict-{Guid.NewGuid():N}";
        await using var context = CreateTransferContext();
        var store = new TransferSubmissionIdempotencyStore(context);
        var original = Fingerprint(100m);

        Assert.Equal(IdempotencyClaimOutcome.Owner,
            (await store.TryClaimAsync(key, original, DateTimeOffset.UtcNow, CancellationToken.None)).Outcome);
        var conflict = await store.TryClaimAsync(key, Fingerprint(101m), DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(IdempotencyClaimOutcome.Conflict, conflict.Outcome);
        var record = await context.IdempotencyRecords.SingleAsync(candidate => candidate.Key == key);
        Assert.Equal(original, record.Fingerprint);
    }

    [Fact]
    public async Task CompletedClaimReplaysOriginalTransferResult()
    {
        var key = $"completed-{Guid.NewGuid():N}";
        var fingerprint = Fingerprint(100m);
        var transferId = Guid.NewGuid();
        await using var context = CreateTransferContext();
        var store = new TransferSubmissionIdempotencyStore(context);
        var owner = await store.TryClaimAsync(key, fingerprint, DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.NotNull(owner.OwnerToken);

        await store.CompleteAsync(
            owner.OwnerToken.Value,
            new TransferSubmissionResult(transferId),
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        var replay = await store.TryClaimAsync(key, fingerprint, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(IdempotencyClaimOutcome.Completed, replay.Outcome);
        Assert.Equal(transferId, replay.Result?.TransferId);
        Assert.Equal(1, await context.IdempotencyRecords.CountAsync(record => record.Key == key));
    }

    [Fact]
    public async Task ConcurrentIdenticalClaimsProduceExactlyOneOwnerWithoutUniqueViolation()
    {
        var key = $"concurrent-{Guid.NewGuid():N}";
        var fingerprint = Fingerprint(100m);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<IdempotencyClaim> ClaimAsync()
        {
            await using var context = CreateTransferContext();
            var store = new TransferSubmissionIdempotencyStore(context);
            await gate.Task;
            return await store.TryClaimAsync(key, fingerprint, DateTimeOffset.UtcNow, CancellationToken.None);
        }

        var claims = new[] { ClaimAsync(), ClaimAsync() };
        gate.SetResult();
        var results = await Task.WhenAll(claims);

        Assert.Equal(1, results.Count(result => result.Outcome == IdempotencyClaimOutcome.Owner));
        Assert.Equal(1, results.Count(result => result.Outcome == IdempotencyClaimOutcome.Processing));
        await using var verification = CreateTransferContext();
        Assert.Equal(1, await verification.IdempotencyRecords.CountAsync(record => record.Key == key));
        Console.WriteLine("TASK-04 concurrency evidence: two simultaneous claims; owner=1, processing duplicate=1, rows=1, PostgreSQL exceptions=0.");
    }

    [Fact]
    public async Task PostgreSqlContainsUniqueScopeAndKeyConstraint()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'transfer_management'
              AND tablename = 'idempotency_records'
              AND indexname = 'ux_idempotency_records_scope_key';
            """;

        var definition = Assert.IsType<string>(await command.ExecuteScalarAsync());
        Assert.Contains("UNIQUE", definition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scope, idempotency_key", definition, StringComparison.OrdinalIgnoreCase);
        Console.WriteLine($"TASK-04 uniqueness evidence: {definition}");
    }

    [Fact]
    public async Task DueProcessStateSurvivesNewApplicationScopeAndPreservesCoordinationMetadata()
    {
        await ClearProcessStatesAsync();
        var createdAt = new DateTimeOffset(2026, 8, 11, 14, 0, 0, TimeSpan.Zero);
        var attemptedAt = createdAt.AddMinutes(1);
        var dueAt = createdAt.AddMinutes(5);
        var correlationId = Guid.NewGuid();
        var transfer = CreateTransfer(createdAt);

        await using (var initialApplicationScope = CreateTransferContext())
        {
            var manager = CreateProcessManager(initialApplicationScope);
            await manager.CreateWithTransferAsync(transfer, correlationId, createdAt, CancellationToken.None);
            await manager.RecordAttemptAsync(transfer.Id, dueAt, attemptedAt, CancellationToken.None);
        }

        await using var restartedApplicationScope = CreateTransferContext();
        var restartedManager = CreateProcessManager(restartedApplicationScope);
        var recovered = Assert.Single(await restartedManager.GetDueAsync(dueAt, 10, CancellationToken.None));

        Assert.Equal(transfer.Id, recovered.TransferId);
        Assert.Equal(correlationId, recovered.CorrelationId);
        Assert.Equal(TransferProcessStatus.Active, recovered.Status);
        Assert.Equal(TransferProcessStep.ActionScheduled, recovered.CurrentStep);
        Assert.Equal(TransferProcessAction.ContinueWorkflow, recovered.NextAction);
        Assert.Equal(1, recovered.AttemptCount);
        Assert.Equal(dueAt, recovered.NextAttemptAtUtc);
        Assert.Equal(createdAt, recovered.CreatedAtUtc);
        Assert.Equal(attemptedAt, recovered.UpdatedAtUtc);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT next_action, attempt_count, next_attempt_at_utc
            FROM transfer_management.transfer_process_states
            WHERE transfer_id = @transfer_id;
            """;
        command.Parameters.AddWithValue("transfer_id", transfer.Id.Value);
        await using var row = await command.ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());
        Assert.Equal("ContinueWorkflow", row.GetString(0));
        Assert.Equal(1, row.GetInt32(1));
        Assert.Equal(dueAt, row.GetFieldValue<DateTimeOffset>(2));
        Console.WriteLine($"TASK-05 restart evidence: transfer={transfer.Id.Value}, correlation={correlationId}, action={row.GetString(0)}, attempts={row.GetInt32(1)}, due={row.GetFieldValue<DateTimeOffset>(2):O}.");
    }

    [Fact]
    public async Task DueQueryExcludesFutureWaitingAndCompletedWorkAndIsDeterministicallyBounded()
    {
        await ClearProcessStatesAsync();
        var now = new DateTimeOffset(2026, 8, 11, 15, 0, 0, TimeSpan.Zero);
        var dueEarlier = CreateTransfer(now);
        var dueTieFirst = CreateTransfer(now);
        var dueTieSecond = CreateTransfer(now);
        var future = CreateTransfer(now);
        var waiting = CreateTransfer(now);
        var completed = CreateTransfer(now);
        var orderedTie = new[] { dueTieFirst, dueTieSecond }.OrderBy(item => item.Id.Value).ToArray();

        await using (var writeScope = CreateTransferContext())
        {
            var manager = CreateProcessManager(writeScope);
            foreach (var transfer in new[] { dueEarlier, dueTieFirst, dueTieSecond, future, waiting, completed })
            {
                await manager.CreateWithTransferAsync(transfer, Guid.NewGuid(), now, CancellationToken.None);
            }

            await manager.ScheduleAsync(dueEarlier.Id, TransferProcessAction.ContinueWorkflow, now.AddMinutes(1), now, CancellationToken.None);
            await manager.ScheduleAsync(dueTieFirst.Id, TransferProcessAction.ContinueWorkflow, now.AddMinutes(2), now, CancellationToken.None);
            await manager.ScheduleAsync(dueTieSecond.Id, TransferProcessAction.ContinueWorkflow, now.AddMinutes(2), now, CancellationToken.None);
            await manager.ScheduleAsync(future.Id, TransferProcessAction.ContinueWorkflow, now.AddMinutes(3), now, CancellationToken.None);
            await manager.MarkWaitingAsync(waiting.Id, now, CancellationToken.None);
            await manager.CompleteAsync(completed.Id, now, CancellationToken.None);
        }

        await using var readScope = CreateTransferContext();
        var results = await CreateProcessManager(readScope).GetDueAsync(now.AddMinutes(2), 2, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal(dueEarlier.Id, results[0].TransferId);
        Assert.Equal(orderedTie[0].Id, results[1].TransferId);
        Assert.DoesNotContain(results, item => item.TransferId == future.Id);
        Assert.DoesNotContain(results, item => item.TransferId == waiting.Id);
        Assert.DoesNotContain(results, item => item.TransferId == completed.Id);

        var allDue = await CreateProcessManager(readScope).GetDueAsync(now.AddMinutes(2), 10, CancellationToken.None);
        Assert.Equal(new[] { dueEarlier.Id, orderedTie[0].Id, orderedTie[1].Id }, allDue.Select(item => item.TransferId));
    }

    [Fact]
    public async Task InvalidProcessUpdateDoesNotCorruptPersistedState()
    {
        await ClearProcessStatesAsync();
        var now = new DateTimeOffset(2026, 8, 11, 16, 0, 0, TimeSpan.Zero);
        var transfer = CreateTransfer(now);
        await using (var writeScope = CreateTransferContext())
        {
            var manager = CreateProcessManager(writeScope);
            await manager.CreateWithTransferAsync(transfer, Guid.NewGuid(), now, CancellationToken.None);
            await Assert.ThrowsAsync<DomainException>(() => manager.ScheduleAsync(
                transfer.Id,
                TransferProcessAction.None,
                now.AddMinutes(1),
                now,
                CancellationToken.None));
        }

        await using var readScope = CreateTransferContext();
        var persisted = await new TransferProcessStateRepository(readScope).GetAsync(transfer.Id, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(TransferProcessStep.Created, persisted.CurrentStep);
        Assert.Equal(TransferProcessAction.ContinueWorkflow, persisted.NextAction);
        Assert.Equal(0, persisted.Version);
    }

    [Fact]
    public void RepositoryAbstractionsDoNotExposeEntityFrameworkCoreTypes()
    {
        var repositoryTypes = new[] { typeof(IAccountRepository), typeof(ITransferRepository), typeof(ITransferProcessStateRepository) };

        var exposedTypes = repositoryTypes
            .SelectMany(type => type.GetMethods())
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType));

        Assert.DoesNotContain(
            exposedTypes,
            type => type.FullName?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true);
    }

    private static string Fingerprint(decimal amount) =>
        TransferSubmissionFingerprint.Create(new TransferSubmissionRequest(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            amount,
            "GBP",
            TransferType.InternalBank));

    private static Transfer CreateTransfer(DateTimeOffset createdAtUtc) =>
        Transfer.Create(Guid.NewGuid(), Guid.NewGuid(), 25m, "GBP", TransferType.InternalBank, createdAtUtc);

    private AccountBalanceDbContext CreateAccountContext() =>
        new(new DbContextOptionsBuilder<AccountBalanceDbContext>().UseNpgsql(
            _connectionString,
            options => options.MigrationsHistoryTable("__EFMigrationsHistory", AccountBalanceDbContext.Schema)).Options);

    private static AccountRepository CreateAccountRepository(AccountBalanceDbContext context) =>
        new AccountRepository(context);

    private TransferManagementDbContext CreateTransferContext() =>
        new(new DbContextOptionsBuilder<TransferManagementDbContext>().UseNpgsql(
            _connectionString,
            options => options.MigrationsHistoryTable("__EFMigrationsHistory", TransferManagementDbContext.Schema)).Options);

    private static TransferRepository CreateTransferRepository(TransferManagementDbContext context) =>
        new TransferRepository(context);

    private static TransferProcessManager CreateProcessManager(TransferManagementDbContext context) =>
        new(new TransferRepository(context), new TransferProcessStateRepository(context));

    private async Task ClearProcessStatesAsync()
    {
        await using var context = CreateTransferContext();
        await context.TransferProcessStates.ExecuteDeleteAsync();
    }
}
