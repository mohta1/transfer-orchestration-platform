using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using TransferOrchestration.PaymentNetwork.Contracts;
using TransferOrchestration.TransferManagement.Application.Observability;
using TransferOrchestration.TransferManagement.Application.Persistence;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Application.Reconciliation;
using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.TransferManagement.Application.PaymentSubmission;

internal interface IPaymentSubmissionProcessStep
{
    Task<PaymentSubmissionStepOutcome> ExecuteAsync(TransferId transferId, long claimedVersion, CancellationToken cancellationToken);
}

internal enum PaymentSubmissionStepOutcome { Accepted, Rejected, StatusUnknown, NotActionable, LostClaim }

internal sealed class PaymentSubmissionProcessStep(
    IServiceScopeFactory scopeFactory,
    IPaymentNetworkGateway paymentNetworkGateway,
    TimeProvider timeProvider,
    ILogger<PaymentSubmissionProcessStep> logger) : IPaymentSubmissionProcessStep
{
    private const int AmbiguityPersistenceAttempts = 3;

    public async Task<PaymentSubmissionStepOutcome> ExecuteAsync(
        TransferId transferId,
        long claimedVersion,
        CancellationToken cancellationToken)
    {
        PaymentSubmissionRequest? request;
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
                return PaymentSubmissionStepOutcome.LostClaim;
            }

            if (transfer.Type != TransferType.DomesticInterbank
                || transfer.State != TransferState.BalanceReserved
                || process.Status != TransferProcessStatus.Active
                || process.NextAction != TransferProcessAction.SubmitToPaymentNetwork)
            {
                return PaymentSubmissionStepOutcome.NotActionable;
            }

            var reference = paymentNetworkGateway.CreateSubmissionReference(transfer.Id.Value);
            var now = timeProvider.GetUtcNow();
            processCorrelationId = process.CorrelationId;
            transfer.BeginExternalSubmission(now);
            process.PrepareExternalSubmission(reference.Value, now);
            try
            {
                // EF's local SaveChanges transaction atomically fences resubmission by
                // persisting the Transfer, reference, and enquiry handoff before I/O.
                await processRepository.SaveChangesAsync(cancellationToken);
            }
            catch (TransferProcessConcurrencyConflictException)
            {
                OperationalTelemetry.LogConcurrencyConflict(
                    logger,
                    transferId.Value,
                    nameof(TransferProcessState),
                    "SaveChanges",
                    processCorrelationId);
                return PaymentSubmissionStepOutcome.LostClaim;
            }

            request = new PaymentSubmissionRequest(
                transfer.Id.Value,
                reference,
                transfer.SourceAccountId,
                transfer.DestinationAccountId,
                transfer.Amount,
                transfer.Currency);
        }

        PaymentSubmissionResult result;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            result = await paymentNetworkGateway.SubmitAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return await PersistAmbiguousOutcomeAsync(transferId, request.Reference, processCorrelationId);
        }
        catch (OperationCanceledException)
        {
            await PersistAmbiguousOutcomeAsync(transferId, request.Reference, processCorrelationId);
            throw;
        }
        catch
        {
            stopwatch.Stop();
            OperationalTelemetry.LogExternalCallCompleted(
                logger,
                transferId.Value,
                "PaymentNetwork",
                "Ambiguous",
                stopwatch.ElapsedMilliseconds,
                processCorrelationId);
            return await PersistAmbiguousOutcomeAsync(transferId, request.Reference, processCorrelationId);
        }

        stopwatch.Stop();
        OperationalTelemetry.LogExternalCallCompleted(
            logger,
            transferId.Value,
            "PaymentNetwork",
            result.ToString(),
            stopwatch.ElapsedMilliseconds,
            processCorrelationId);

        var outcome = result == PaymentSubmissionResult.Timeout
            ? await PersistAmbiguousOutcomeAsync(transferId, request.Reference, processCorrelationId)
            : await PersistOutcomeAsync(transferId, request.Reference, result);
        cancellationToken.ThrowIfCancellationRequested();
        return outcome;
    }

    private async Task<PaymentSubmissionStepOutcome> PersistAmbiguousOutcomeAsync(
        TransferId transferId,
        NetworkSubmissionReference reference,
        Guid correlationId)
    {
        for (var attempt = 1; attempt <= AmbiguityPersistenceAttempts; attempt++)
        {
            await using var outcomeScope = scopeFactory.CreateAsyncScope();
            var transferRepository = outcomeScope.ServiceProvider.GetRequiredService<ITransferRepository>();
            var processRepository = outcomeScope.ServiceProvider.GetRequiredService<ITransferProcessStateRepository>();
            var transfer = await transferRepository.GetByIdAsync(transferId, CancellationToken.None)
                ?? throw new InvalidOperationException($"Transfer '{transferId.Value}' was not found.");
            var process = await processRepository.GetAsync(transferId, CancellationToken.None)
                ?? throw new InvalidOperationException($"Transfer process '{transferId.Value}' was not found.");

            var referenceMatches = string.Equals(
                process.NetworkSubmissionReference,
                reference.Value,
                StringComparison.Ordinal);
            if (!referenceMatches)
            {
                throw new InvalidOperationException(
                    $"Payment submission reference mismatch for transfer '{transferId.Value}'.");
            }

            if (transfer.State == TransferState.SubmissionStatusUnknown
                && process.Status == TransferProcessStatus.Active
                && process.NextAction == TransferProcessAction.EnquirePaymentStatus)
            {
                return PaymentSubmissionStepOutcome.StatusUnknown;
            }

            if (transfer.State is TransferState.SettlementPending or TransferState.Rejected)
            {
                return PaymentSubmissionStepOutcome.StatusUnknown;
            }

            if (transfer.State != TransferState.PendingExternalSubmission
                || process.Status != TransferProcessStatus.Active
                || process.NextAction != TransferProcessAction.EnquirePaymentStatus)
            {
                throw new InvalidOperationException(
                    $"Cannot persist an ambiguous payment outcome from transfer state '{transfer.State}' " +
                    $"and process action '{process.NextAction}'.");
            }

            transfer.MarkSubmissionStatusUnknown(timeProvider.GetUtcNow());
            await outcomeScope.ServiceProvider.GetRequiredService<IReconciliationScheduling>()
                .EnsureScheduledAsync(transferId, reference.Value, timeProvider.GetUtcNow(), CancellationToken.None);
            try
            {
                await processRepository.SaveChangesAsync(CancellationToken.None);
                OperationalTelemetry.LogSubmissionStatusUnknown(
                    logger,
                    transferId.Value,
                    "PaymentNetwork",
                    correlationId);
                return PaymentSubmissionStepOutcome.StatusUnknown;
            }
            catch (TransferProcessConcurrencyConflictException) when (attempt < AmbiguityPersistenceAttempts)
            {
                // Reload and re-evaluate: never report ambiguity while the durable
                // Transfer could still be PendingExternalSubmission.
            }
        }

        throw new InvalidOperationException(
            $"Could not durably persist the ambiguous payment outcome for transfer '{transferId.Value}'.");
    }

    private async Task<PaymentSubmissionStepOutcome> PersistOutcomeAsync(
        TransferId transferId,
        NetworkSubmissionReference reference,
        PaymentSubmissionResult result)
    {
        await using var outcomeScope = scopeFactory.CreateAsyncScope();
        var transferRepository = outcomeScope.ServiceProvider.GetRequiredService<ITransferRepository>();
        var processRepository = outcomeScope.ServiceProvider.GetRequiredService<ITransferProcessStateRepository>();
        var transfer = await transferRepository.GetByIdAsync(transferId, CancellationToken.None)
            ?? throw new InvalidOperationException($"Transfer '{transferId.Value}' was not found.");
        var process = await processRepository.GetAsync(transferId, CancellationToken.None)
            ?? throw new InvalidOperationException($"Transfer process '{transferId.Value}' was not found.");

        if (transfer.State != TransferState.PendingExternalSubmission
            || process.NextAction != TransferProcessAction.EnquirePaymentStatus
            || !string.Equals(process.NetworkSubmissionReference, reference.Value, StringComparison.Ordinal))
        {
            return PaymentSubmissionStepOutcome.StatusUnknown;
        }

        var now = timeProvider.GetUtcNow();
        var outcome = result switch
        {
            PaymentSubmissionResult.Accepted => Accept(transfer, process, now),
            PaymentSubmissionResult.Rejected => Reject(transfer, process, now),
            _ => throw new InvalidOperationException("Unsupported payment submission result.")
        };

        try
        {
            await processRepository.SaveChangesAsync(CancellationToken.None);
            return outcome;
        }
        catch (TransferProcessConcurrencyConflictException)
        {
            // A newer process snapshot wins. The durable action was already fenced to
            // enquiry before the call, so losing this race can never re-arm submission.
            return PaymentSubmissionStepOutcome.StatusUnknown;
        }
    }

    private static PaymentSubmissionStepOutcome Accept(Transfer transfer, TransferProcessState process, DateTimeOffset now)
    {
        transfer.MarkSettlementPending(now);
        process.MarkWaiting(now);
        return PaymentSubmissionStepOutcome.Accepted;
    }

    private static PaymentSubmissionStepOutcome Reject(Transfer transfer, TransferProcessState process, DateTimeOffset now)
    {
        transfer.RejectExternalSubmission(now);
        process.Schedule(TransferProcessAction.ReleaseReservation, now, now);
        return PaymentSubmissionStepOutcome.Rejected;
    }
}
