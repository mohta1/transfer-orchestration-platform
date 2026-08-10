using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;

namespace TransferOrchestration.TransferManagement;

public static class DependencyInjection
{
    public static IServiceCollection AddTransferManagementModule(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<TransferManagementDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsqlOptions =>
                    npgsqlOptions.MigrationsHistoryTable(
                        "__EFMigrationsHistory",
                        TransferManagementDbContext.Schema)));

        return services;
    }
}
