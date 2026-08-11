using Microsoft.EntityFrameworkCore;
using Npgsql;
using TransferOrchestration.AccountBalance.Application.Persistence;
using TransferOrchestration.AccountBalance.Domain.Accounts;
using TransferOrchestration.AccountBalance.Infrastructure.Persistence;
using TransferOrchestration.AccountBalance.Infrastructure.Persistence.Repositories;
using TransferOrchestration.TransferManagement.Application.Persistence;
using TransferOrchestration.TransferManagement.Domain.Transfers;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence.Repositories;

namespace TransferOrchestration.IntegrationTests;

public sealed class PersistenceMappingTests : IAsyncLifetime
{
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING")
        ?? "Host=localhost;Port=5432;Database=transfer_orchestration;Username=transfer_app;Password=transfer_test";

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
                ('account_balance', 'accounts'),
                ('account_balance', 'balance_reservations'));
            """;

        Assert.Equal(3L, await command.ExecuteScalarAsync());
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
        var reloaded = await readRepository.GetByIdAsync(account.Id, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Equal(375m, reloaded.AvailableBalance);
        Assert.Equal(125m, reloaded.ReservedBalance);
        Assert.Equal(transferId, Assert.Single(reloaded.Reservations).TransferId);
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
        var first = await firstRepository.GetByIdAsync(account.Id, CancellationToken.None);
        var stale = await staleRepository.GetByIdAsync(account.Id, CancellationToken.None);
        Assert.NotNull(first);
        Assert.NotNull(stale);
        Assert.Equal(0, first.Version);
        Assert.Equal(0, stale.Version);

        Assert.True(firstContext.Model.FindEntityType(typeof(Account))!
            .FindProperty(nameof(Account.Version))!.IsConcurrencyToken);

        first.Reserve(Guid.NewGuid(), 10m, DateTimeOffset.UtcNow);
        stale.Reserve(Guid.NewGuid(), 20m, DateTimeOffset.UtcNow);
        await firstRepository.SaveChangesAsync(CancellationToken.None);
        var conflict = await Assert.ThrowsAsync<AccountConcurrencyConflictException>(
            () => staleRepository.SaveChangesAsync(CancellationToken.None));
        Assert.Equal(account.Id.Value, conflict.AccountId);

        await using var reloadContext = CreateAccountContext();
        var reloadRepository = CreateAccountRepository(reloadContext);
        var winner = await reloadRepository.GetByIdAsync(account.Id, CancellationToken.None);
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
    public void RepositoryAbstractionsDoNotExposeEntityFrameworkCoreTypes()
    {
        var repositoryTypes = new[] { typeof(IAccountRepository), typeof(ITransferRepository) };

        var exposedTypes = repositoryTypes
            .SelectMany(type => type.GetMethods())
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType));

        Assert.DoesNotContain(
            exposedTypes,
            type => type.FullName?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true);
    }

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
}
