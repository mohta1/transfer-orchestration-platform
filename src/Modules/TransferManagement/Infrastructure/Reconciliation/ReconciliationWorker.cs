using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TransferOrchestration.TransferManagement.Application.Reconciliation;

namespace TransferOrchestration.TransferManagement.Infrastructure.Reconciliation;

internal sealed class ReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ReconciliationOptions> options,
    ILogger<ReconciliationWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> LogDispatchFailure =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1, nameof(ReconciliationWorker)),
            "Reconciliation due-work dispatch failed.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IReconciliationDueWorkDispatcher>()
                    .DispatchDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogDispatchFailure(logger, exception);
            }
        }
    }
}
