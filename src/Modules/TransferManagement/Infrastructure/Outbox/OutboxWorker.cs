using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TransferOrchestration.TransferManagement.Infrastructure.Outbox;

internal sealed class OutboxWorker(IServiceScopeFactory scopeFactory, IOptions<OutboxOptions> options, ILogger<OutboxWorker> logger)
    : BackgroundService
{
    private static readonly Action<ILogger, string, Exception?> BatchFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(1, nameof(BatchFailed)),
            "Outbox batch failed for worker {WorkerInstanceId}");
    private readonly string _workerId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<OutboxBatchDispatcher>()
                    .DispatchBatchAsync(_workerId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                BatchFailed(logger, _workerId, exception);
            }
        }
    }
}
