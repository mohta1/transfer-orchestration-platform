using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using TransferOrchestration.TransferManagement.Application.Observability;
using TransferOrchestration.TransferManagement.Application.Persistence;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Domain.Transfers;
using TransferOrchestration.TransferManagement.Infrastructure.FraudScreening;

namespace TransferOrchestration.TransferManagement.Application.FraudScreening;

internal interface IFraudScreeningProcessStep
{
    Task<FraudScreeningStepOutcome> ExecuteAsync(
        TransferId transferId,
        long claimedVersion,
        CancellationToken cancellationToken);
}

internal enum FraudScreeningStepOutcome
{
    Approved,
    Rejected,
    ManualReviewRequired,
    RetryScheduled,
    EscalatedToManualReview,
    NotActionable,
    LostClaim
}

internal sealed class FraudScreeningProcessStep(
    IServiceScopeFactory scopeFactory,
    IFraudScreening fraudScreening,
    IOptions<FraudScreeningOptions> options,
    TimeProvider timeProvider,
    ILogger<FraudScreeningProcessStep> logger) : IFraudScreeningProcessStep
{
    public async Task<FraudScreeningStepOutcome> ExecuteAsync(
        TransferId transferId,
        long claimedVersion,
        CancellationToken cancellationToken)
    {
        FraudScreeningRequest? screeningRequest;
        Guid processCorrelationId;
        await using (var preparationScope = scopeFactory.CreateAsyncScope())
        {
            var transferRepository = preparationScope.ServiceProvider.GetRequiredService<ITransferRepository>();
            var processRepository = preparationScope.ServiceProvider.GetRequiredService<ITransferProcessStateRepository>();
            var transfer = await transferRepository.GetByIdAsync(transferId, cancellationToken)
                ?? throw new InvalidOperationException($"Transfer '{transferId.Value}' was not found.");
            var process = await processRepository.GetAsync(transferId, cancellationToken)
                ?? throw new InvalidOperationException($"Transfer process '{transferId.Value}' was not found.");

            if (process.Version != claimedVersion)
            {
                return FraudScreeningStepOutcome.LostClaim;
            }

            if (IsTerminalOrProgressed(transfer.State))
            {
                return FraudScreeningStepOutcome.NotActionable;
            }

            if (transfer.State != TransferState.PendingFraudScreening
                || process.Status != TransferProcessStatus.Active
                || process.NextAction != TransferProcessAction.RequestFraudScreening)
            {
                return FraudScreeningStepOutcome.NotActionable;
            }

            processCorrelationId = process.CorrelationId;
            screeningRequest = new FraudScreeningRequest(
                transfer.Id.Value,
                transfer.SourceAccountId,
                transfer.DestinationAccountId,
                transfer.Amount,
                transfer.Currency,
                transfer.Type);
        }

        FraudScreeningResult result;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            result = await fraudScreening.ScreenAsync(screeningRequest, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            result = FraudScreeningResult.TemporarilyUnavailable;
        }
        finally
        {
            stopwatch.Stop();
        }

        OperationalTelemetry.LogExternalCallCompleted(
            logger,
            transferId.Value,
            "FraudScreening",
            result.ToString(),
            stopwatch.ElapsedMilliseconds,
            processCorrelationId);

        return await PersistOutcomeAsync(transferId, claimedVersion, result, cancellationToken);
    }

    private async Task<FraudScreeningStepOutcome> PersistOutcomeAsync(
        TransferId transferId,
        long claimedVersion,
        FraudScreeningResult result,
        CancellationToken cancellationToken)
    {
        await using var outcomeScope = scopeFactory.CreateAsyncScope();
        var transferRepository = outcomeScope.ServiceProvider.GetRequiredService<ITransferRepository>();
        var processRepository = outcomeScope.ServiceProvider.GetRequiredService<ITransferProcessStateRepository>();
        var transfer = await transferRepository.GetByIdAsync(transferId, cancellationToken)
            ?? throw new InvalidOperationException($"Transfer '{transferId.Value}' was not found.");
        var process = await processRepository.GetAsync(transferId, cancellationToken)
            ?? throw new InvalidOperationException($"Transfer process '{transferId.Value}' was not found.");

        if (process.Version != claimedVersion)
        {
            return FraudScreeningStepOutcome.LostClaim;
        }

        if (IsTerminalOrProgressed(transfer.State))
        {
            return FraudScreeningStepOutcome.NotActionable;
        }

        if (transfer.State != TransferState.PendingFraudScreening
            || process.Status != TransferProcessStatus.Active
            || process.NextAction != TransferProcessAction.RequestFraudScreening)
        {
            return FraudScreeningStepOutcome.NotActionable;
        }

        var now = timeProvider.GetUtcNow();
        var screeningOptions = options.Value;

        switch (result)
        {
            case FraudScreeningResult.Approved:
                OperationalTelemetry.LogStateTransition(
                    logger,
                    transferId.Value,
                    TransferState.PendingFraudScreening.ToString(),
                    TransferState.PendingBalanceReservation.ToString(),
                    TransferProcessAction.ReserveBalance.ToString(),
                    process.CorrelationId,
                    null);
                transfer.RequestBalanceReservation(now);
                process.Schedule(TransferProcessAction.ReserveBalance, now, now);
                await processRepository.SaveChangesAsync(cancellationToken);
                return FraudScreeningStepOutcome.Approved;

            case FraudScreeningResult.Rejected:
                transfer.RejectForFraud(now);
                process.Complete(now);
                await processRepository.SaveChangesAsync(cancellationToken);
                return FraudScreeningStepOutcome.Rejected;

            case FraudScreeningResult.ManualReviewRequired:
                transfer.EscalateFraudToManualReview(now);
                process.Complete(now);
                await processRepository.SaveChangesAsync(cancellationToken);
                return FraudScreeningStepOutcome.ManualReviewRequired;

            case FraudScreeningResult.Timeout:
            case FraudScreeningResult.TemporarilyUnavailable:
                var attemptCountAfterFailure = process.AttemptCount + 1;
                if (FraudScreeningRetryPolicy.ShouldEscalate(screeningOptions, attemptCountAfterFailure))
                {
                    transfer.EscalateFraudToManualReview(now);
                    process.Complete(now);
                    await processRepository.SaveChangesAsync(cancellationToken);
                    return FraudScreeningStepOutcome.EscalatedToManualReview;
                }

                process.RecordAttempt(
                    FraudScreeningRetryPolicy.CalculateNextAttempt(
                        screeningOptions,
                        attemptCountAfterFailure,
                        now),
                    now);
                OperationalTelemetry.LogRetryScheduled(
                    logger,
                    transferId.Value,
                    process.NextAction.ToString(),
                    process.AttemptCount,
                    process.NextAttemptAtUtc!.Value,
                    process.CorrelationId);
                await processRepository.SaveChangesAsync(cancellationToken);
                return FraudScreeningStepOutcome.RetryScheduled;

            default:
                throw new InvalidOperationException($"Unsupported fraud screening result '{result}'.");
        }
    }

    private static bool IsTerminalOrProgressed(TransferState state) =>
        state is TransferState.FraudRejected
            or TransferState.PendingBalanceReservation
            or TransferState.ManualReviewRequired
            or TransferState.Rejected
            or TransferState.Completed;
}
