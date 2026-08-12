using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace TransferOrchestration.AuditOperations.Infrastructure.Persistence;

public sealed class AuditOperationsDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<AuditOperationsDbContext>
{
    public AuditOperationsDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Database")
            ?? "Host=localhost;Database=transfer_orchestration;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<AuditOperationsDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable(
                "__EFMigrationsHistory",
                AuditOperationsDbContext.Schema));

        return new AuditOperationsDbContext(optionsBuilder.Options);
    }
}
