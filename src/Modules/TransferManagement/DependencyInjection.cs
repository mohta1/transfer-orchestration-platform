using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransferOrchestration.TransferManagement.Application.Persistence;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence.Repositories;

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
        services.AddScoped<ITransferRepository, TransferRepository>();

        return services;
    }
}
