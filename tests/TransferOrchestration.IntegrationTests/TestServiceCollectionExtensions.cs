using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace TransferOrchestration.IntegrationTests;

internal static class TestServiceCollectionExtensions
{
    /// <summary>
    /// Configures a bare <see cref="ServiceCollection"/> for manual integration tests:
    /// registers logging for process-step telemetry and removes background workers so
    /// tests do not race with polling workers for the same PostgreSQL rows.
    /// </summary>
    public static IServiceCollection ConfigureManualIntegrationTestHost(this IServiceCollection services)
    {
        services.AddLogging();
        services.RemoveAll<IHostedService>();
        return services;
    }

    /// <summary>
    /// Removes hosted background workers registered by the application host.
    /// Call from WebApplicationFactory.ConfigureServices after module registration.
    /// </summary>
    public static IServiceCollection RemoveHostedWorkers(this IServiceCollection services)
    {
        services.RemoveAll<IHostedService>();
        return services;
    }
}
