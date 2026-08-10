using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransferOrchestration.AccountBalance.Infrastructure.Persistence;

namespace TransferOrchestration.AccountBalance;

public static class DependencyInjection
{
    public static IServiceCollection AddAccountBalanceModule(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<AccountBalanceDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsqlOptions =>
                    npgsqlOptions.MigrationsHistoryTable(
                        "__EFMigrationsHistory",
                        AccountBalanceDbContext.Schema)));

        return services;
    }
}
