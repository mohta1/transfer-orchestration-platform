using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TransferOrchestration.TransferManagement.Application.FraudScreening;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Application.PaymentSubmission;

namespace TransferOrchestration.TransferManagement.Infrastructure.Processing;

internal sealed class TransferProcessWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<TransferProcessWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly Action<ILogger, Exception?> LogDispatchFailure =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1, nameof(TransferProcessWorker)),
            "Transfer process due-work dispatch failed.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(PollInterval, stoppingToken);

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var fraudDispatcher = scope.ServiceProvider.GetRequiredService<IFraudScreeningDueWorkDispatcher>();
                await fraudDispatcher.DispatchDueAsync(stoppingToken);
                var dispatcher = scope.ServiceProvider.GetRequiredService<ITransferProcessDueWorkDispatcher>();
                await dispatcher.DispatchDueAsync(stoppingToken);
                var paymentDispatcher = scope.ServiceProvider.GetRequiredService<IPaymentSubmissionDueWorkDispatcher>();
                await paymentDispatcher.DispatchDueAsync(stoppingToken);
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
