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
            ?? Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Database")
            ?? "Host=localhost;Port=5432;Database=transfer_orchestration;Username=transfer_app;Password=transfer_test";

        var optionsBuilder = new DbContextOptionsBuilder<AuditOperationsDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable(
                "__EFMigrationsHistory",
                AuditOperationsDbContext.Schema));

        return new AuditOperationsDbContext(optionsBuilder.Options);
    }
}
