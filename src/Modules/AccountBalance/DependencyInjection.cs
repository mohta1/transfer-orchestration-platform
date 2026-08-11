using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransferOrchestration.AccountBalance.Application.Persistence;
using TransferOrchestration.AccountBalance.Application.Reservations;
using TransferOrchestration.AccountBalance.Contracts;
using TransferOrchestration.AccountBalance.Infrastructure.Persistence;
using TransferOrchestration.AccountBalance.Infrastructure.Persistence.Repositories;

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
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IAccountBalanceReservations, AccountBalanceReservations>();
        services.AddScoped<IReservationAttemptObserver, NoOpReservationAttemptObserver>();
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
