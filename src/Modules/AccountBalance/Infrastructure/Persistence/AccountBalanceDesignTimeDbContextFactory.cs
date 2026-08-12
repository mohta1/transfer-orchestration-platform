using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TransferOrchestration.AccountBalance.Infrastructure.Persistence;

internal sealed class AccountBalanceDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<AccountBalanceDbContext>
{
    public AccountBalanceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Database")
            ?? "Host=localhost;Port=5432;Database=transfer_orchestration;Username=transfer_app;Password=transfer_test";

        var optionsBuilder = new DbContextOptionsBuilder<AccountBalanceDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsqlOptions =>
                npgsqlOptions.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    AccountBalanceDbContext.Schema));

        return new AccountBalanceDbContext(optionsBuilder.Options);
    }
}
