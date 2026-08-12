using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransferOrchestration.AuditOperations.Application;
using TransferOrchestration.AuditOperations.Contracts;
using TransferOrchestration.AuditOperations.Infrastructure.Correlation;
using TransferOrchestration.AuditOperations.Infrastructure.Persistence;

namespace TransferOrchestration.AuditOperations;

public static class DependencyInjection
{
    public static IServiceCollection AddAuditOperationsModule(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<AuditOperationsDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    AuditOperationsDbContext.Schema)));

        services.AddScoped<ICorrelationContext, CorrelationContext>();
        services.AddScoped<IOperatorContext, OperatorContext>();
        services.AddScoped<IOperationsAuditWriter, OperationsAuditWriter>();

        return services;
    }
}
