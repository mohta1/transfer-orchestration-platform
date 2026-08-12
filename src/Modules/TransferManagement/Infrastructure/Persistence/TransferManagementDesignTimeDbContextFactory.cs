using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TransferOrchestration.TransferManagement.Infrastructure.Persistence;

internal sealed class TransferManagementDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<TransferManagementDbContext>
{
    public TransferManagementDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=transfer_orchestration;Username=transfer_app;Password=transfer_test";

        var optionsBuilder = new DbContextOptionsBuilder<TransferManagementDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsqlOptions =>
                npgsqlOptions.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    TransferManagementDbContext.Schema));

        return new TransferManagementDbContext(optionsBuilder.Options);
    }
}
