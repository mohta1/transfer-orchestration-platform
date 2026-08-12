using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using TransferOrchestration.Notification.Application;
using TransferOrchestration.Notification.Contracts;
using TransferOrchestration.Notification.Infrastructure;
using TransferOrchestration.Notification.Infrastructure.Persistence;
using TransferOrchestration.TransferManagement.Contracts.IntegrationEvents;

namespace TransferOrchestration.Notification;

public static class DependencyInjection
{
    public static IServiceCollection AddNotificationModule(this IServiceCollection services, string connectionString,
        IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContext<NotificationDbContext>(options => options.UseNpgsql(connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", NotificationDbContext.Schema)));
        services.AddSingleton<INotificationProvider, LoggingNotificationProvider>();
        services.AddScoped<IIntegrationEventDispatcher, TransferCompletedNotificationConsumer>();
        services.AddSingleton(TimeProvider.System);
        var effectiveConfiguration = configuration ?? new ConfigurationBuilder().Build();
        services.AddOptions<LoggingNotificationProviderOptions>()
            .Bind(effectiveConfiguration.GetSection(LoggingNotificationProviderOptions.SectionName))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<NotificationConsumerOptions>()
            .Bind(effectiveConfiguration.GetSection(NotificationConsumerOptions.SectionName))
            .ValidateDataAnnotations().ValidateOnStart();
        return services;
    }
}
