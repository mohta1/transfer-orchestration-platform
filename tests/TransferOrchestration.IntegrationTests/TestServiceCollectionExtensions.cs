using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace TransferOrchestration.IntegrationTests;

internal static class TestServiceCollectionExtensions
{
    /// <summary>
    /// Removes hosted background workers so tests that drive process steps manually
    /// do not race with polling workers for the same PostgreSQL rows.
    /// </summary>
    public static IServiceCollection RemoveHostedWorkers(this IServiceCollection services)
    {
        services.RemoveAll<IHostedService>();
        return services;
    }
}
