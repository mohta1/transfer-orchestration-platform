using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TransferOrchestration.Notification.Application;
using TransferOrchestration.Notification.Contracts;
using TransferOrchestration.Notification.Infrastructure;
using TransferOrchestration.Notification.Infrastructure.Persistence;
using TransferOrchestration.TransferManagement.Contracts.IntegrationEvents;

namespace TransferOrchestration.Notification;

public static class DependencyInjection
{
    public static IServiceCollection AddNotificationModule(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContext<NotificationDbContext>(options => options.UseNpgsql(connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", NotificationDbContext.Schema)));
        services.AddScoped<INotificationProvider, LoggingNotificationProvider>();
        services.AddScoped<IIntegrationEventDispatcher, TransferCompletedNotificationConsumer>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
