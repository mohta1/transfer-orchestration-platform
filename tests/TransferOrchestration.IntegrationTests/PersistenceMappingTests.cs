using Microsoft.EntityFrameworkCore;
using Npgsql;
using TransferOrchestration.AccountBalance.Domain.Accounts;
using TransferOrchestration.AccountBalance.Infrastructure.Persistence;
using TransferOrchestration.TransferManagement.Domain.Transfers;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;

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
    public async Task TransferPersistsAndReloads()
    {
        var transfer = Transfer.Create(Guid.NewGuid(), Guid.NewGuid(), 125.50m, "GBP", TransferType.InternalBank, DateTimeOffset.UtcNow);
        await using (var context = CreateTransferContext())
        {
            context.Transfers.Add(transfer);
            await context.SaveChangesAsync();
        }

        await using var readContext = CreateTransferContext();
        var reloaded = await readContext.Transfers.SingleAsync(candidate => candidate.Id == transfer.Id);
        Assert.Equal(transfer.Id, reloaded.Id);
        Assert.Equal(125.50m, reloaded.Amount);
        Assert.Equal("GBP", reloaded.Currency);
    }

    [Fact]
    public async Task AccountWithReservationPersistsAndReloads()
    {
        var account = Account.Create(Guid.NewGuid(), "GBP", 500m);
        var transferId = Guid.NewGuid();
        account.Reserve(transferId, 125m, DateTimeOffset.UtcNow);
        await using (var context = CreateAccountContext())
        {
            context.Accounts.Add(account);
            await context.SaveChangesAsync();
        }

        await using var readContext = CreateAccountContext();
        var reloaded = await readContext.Accounts.Include(candidate => candidate.Reservations)
            .SingleAsync(candidate => candidate.Id == account.Id);
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
    public async Task AccountVersionIsAConcurrencyTokenAndRejectsAStaleUpdate()
    {
        var account = Account.Create(Guid.NewGuid(), "GBP", 100m);
        await using (var setup = CreateAccountContext())
        {
            setup.Accounts.Add(account);
            await setup.SaveChangesAsync();
        }

        await using var firstContext = CreateAccountContext();
        await using var secondContext = CreateAccountContext();
        var first = await firstContext.Accounts.SingleAsync(candidate => candidate.Id == account.Id);
        var stale = await secondContext.Accounts.SingleAsync(candidate => candidate.Id == account.Id);

        Assert.True(firstContext.Model.FindEntityType(typeof(Account))!
            .FindProperty(nameof(Account.Version))!.IsConcurrencyToken);

        first.Reserve(Guid.NewGuid(), 10m, DateTimeOffset.UtcNow);
        stale.Reserve(Guid.NewGuid(), 20m, DateTimeOffset.UtcNow);
        await firstContext.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());
    }

    private AccountBalanceDbContext CreateAccountContext() =>
        new(new DbContextOptionsBuilder<AccountBalanceDbContext>().UseNpgsql(
            _connectionString,
            options => options.MigrationsHistoryTable("__EFMigrationsHistory", AccountBalanceDbContext.Schema)).Options);

    private TransferManagementDbContext CreateTransferContext() =>
        new(new DbContextOptionsBuilder<TransferManagementDbContext>().UseNpgsql(
            _connectionString,
            options => options.MigrationsHistoryTable("__EFMigrationsHistory", TransferManagementDbContext.Schema)).Options);
}
