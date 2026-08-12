using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TransferOrchestration.AccountBalance.Contracts;
using TransferOrchestration.PaymentNetwork.Contracts;
using TransferOrchestration.TransferManagement.Application.Persistence;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Domain.Transfers;
using TransferOrchestration.TransferManagement.Infrastructure.Reconciliation;

namespace TransferOrchestration.TransferManagement.Application.Reconciliation;

internal interface IReconciliationProcessStep
{
    Task<ReconciliationStepOutcome> ExecuteAsync(
        ReconciliationClaim claim,
        CancellationToken cancellationToken);
}

internal enum ReconciliationStepOutcome
{
    Settled,
    Rejected,
    StillUnknown,
    ManualReviewRequired,
    EnquiryFailed,
    NotActionable,
    LostClaim
}

internal sealed class ReconciliationProcessStep(
    IServiceScopeFactory scopeFactory,
    IPaymentNetworkGateway paymentNetworkGateway,
    IOptions<ReconciliationOptions> options,
    TimeProvider timeProvider) : IReconciliationProcessStep
{
    public async Task<ReconciliationStepOutcome> ExecuteAsync(
        ReconciliationClaim claim,
        CancellationToken cancellationToken)
    {
        NetworkSubmissionReference reference;
        await using (var preparationScope = scopeFactory.CreateAsyncScope())
        {
            var recordRepository = preparationScope.ServiceProvider.GetRequiredService<IReconciliationRecordRepository>();
            var record = await recordRepository.GetByTransferIdAsync(claim.TransferId, cancellationToken);
            if (record is null
                || record.Id != claim.Id
                || record.Version != claim.Version
                || record.Status != ReconciliationStatus.Active)
            {
                return ReconciliationStepOutcome.LostClaim;
            }

            var transferRepository = preparationScope.ServiceProvider.GetRequiredService<ITransferRepository>();
            var transfer = await transferRepository.GetByIdAsync(claim.TransferId, cancellationToken);
            if (transfer is null
                || transfer.Type != TransferType.DomesticInterbank)
            {
                return ReconciliationStepOutcome.NotActionable;
            }

            if (transfer.State == TransferState.SettlementPending)
            {
                return await PersistOutcomeAsync(claim, PaymentStatusResult.Settled, cancellationToken);
            }

            if (transfer.State != TransferState.SubmissionStatusUnknown)
            {
                return ReconciliationStepOutcome.NotActionable;
            }

            reference = new NetworkSubmissionReference(record.NetworkSubmissionReference);
        }

        PaymentStatusResult enquiryResult;
        try
        {
            enquiryResult = await paymentNetworkGateway.GetStatusAsync(reference, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return await PersistEnquiryFailureAsync(claim, exception.Message, cancellationToken);
        }

        return await PersistOutcomeAsync(claim, enquiryResult, cancellationToken);
    }

    private async Task<ReconciliationStepOutcome> PersistOutcomeAsync(
        ReconciliationClaim claim,
        PaymentStatusResult enquiryResult,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var recordRepository = scope.ServiceProvider.GetRequiredService<IReconciliationRecordRepository>();
        var transferRepository = scope.ServiceProvider.GetRequiredService<ITransferRepository>();
        var processRepository = scope.ServiceProvider.GetRequiredService<ITransferProcessStateRepository>();
        var finalization = scope.ServiceProvider.GetRequiredService<IAccountBalanceReservationFinalization>();

        var record = await recordRepository.GetByTransferIdAsync(claim.TransferId, cancellationToken);
        if (record is null
            || record.Id != claim.Id
            || record.Version != claim.Version
            || record.Status != ReconciliationStatus.Active)
        {
            return ReconciliationStepOutcome.LostClaim;
        }

        var transfer = await transferRepository.GetByIdAsync(claim.TransferId, cancellationToken)
            ?? throw new InvalidOperationException($"Transfer '{claim.TransferId.Value}' was not found.");
        var process = await processRepository.GetAsync(claim.TransferId, cancellationToken)
            ?? throw new InvalidOperationException($"Transfer process '{claim.TransferId.Value}' was not found.");

        if (transfer.State == TransferState.Completed)
        {
            record.Close(timeProvider.GetUtcNow(), nameof(PaymentStatusResult.Settled));
            await processRepository.SaveChangesAsync(cancellationToken);
            return ReconciliationStepOutcome.Settled;
        }

        if (transfer.State == TransferState.Rejected)
        {
            record.Close(timeProvider.GetUtcNow(), nameof(PaymentStatusResult.Rejected));
            await processRepository.SaveChangesAsync(cancellationToken);
            return ReconciliationStepOutcome.Rejected;
        }

        if (transfer.State == TransferState.ManualReviewRequired)
        {
            record.EscalateToManualReview(timeProvider.GetUtcNow());
            await processRepository.SaveChangesAsync(cancellationToken);
            return ReconciliationStepOutcome.ManualReviewRequired;
        }

        if (transfer.State == TransferState.SettlementPending)
        {
            if (enquiryResult != PaymentStatusResult.Settled)
            {
                return ReconciliationStepOutcome.NotActionable;
            }

            return await PersistSettledAsync(
                record, transfer, process, finalization, timeProvider.GetUtcNow(), processRepository, cancellationToken);
        }

        if (transfer.State != TransferState.SubmissionStatusUnknown)
        {
            return ReconciliationStepOutcome.NotActionable;
        }

        var now = timeProvider.GetUtcNow();
        ReconciliationStepOutcome outcome = enquiryResult switch
        {
            PaymentStatusResult.Settled => await PersistSettledAsync(
                record, transfer, process, finalization, now, processRepository, cancellationToken),
            PaymentStatusResult.Rejected => await PersistRejectedAsync(
                record, transfer, process, finalization, now, processRepository, cancellationToken),
            PaymentStatusResult.Unknown => await PersistStillUnknownAsync(
                record, transfer, process, now, processRepository, cancellationToken),
            PaymentStatusResult.Accepted => await PersistStillUnknownAsync(
                record, transfer, process, now, processRepository, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported payment status result '{enquiryResult}'.")
        };

        return outcome;
    }

    private static async Task<ReconciliationStepOutcome> PersistSettledAsync(
        ReconciliationRecord record,
        Transfer transfer,
        TransferProcessState process,
        IAccountBalanceReservationFinalization finalization,
        DateTimeOffset now,
        ITransferProcessStateRepository processRepository,
        CancellationToken cancellationToken)
    {
        if (transfer.State != TransferState.SubmissionStatusUnknown
            && transfer.State != TransferState.SettlementPending)
        {
            return ReconciliationStepOutcome.NotActionable;
        }

        if (transfer.State == TransferState.SubmissionStatusUnknown)
        {
            transfer.MarkSettlementPending(now);
        }

        var consume = await finalization.ConsumeAsync(
            new FinalizeFundsRequest(transfer.Id.Value, transfer.SourceAccountId),
            cancellationToken);
        if (!consume.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Could not consume reservation for transfer '{transfer.Id.Value}': {consume.Outcome}.");
        }

        transfer.CompleteSettlement(now);
        record.Close(now, nameof(PaymentStatusResult.Settled));
        process.Complete(now);
        await processRepository.SaveChangesAsync(cancellationToken);
        return ReconciliationStepOutcome.Settled;
    }

    private static async Task<ReconciliationStepOutcome> PersistRejectedAsync(
        ReconciliationRecord record,
        Transfer transfer,
        TransferProcessState process,
        IAccountBalanceReservationFinalization finalization,
        DateTimeOffset now,
        ITransferProcessStateRepository processRepository,
        CancellationToken cancellationToken)
    {
        transfer.RejectExternalSubmission(now);

        var release = await finalization.ReleaseAsync(
            new FinalizeFundsRequest(transfer.Id.Value, transfer.SourceAccountId),
            cancellationToken);
        if (!release.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Could not release reservation for transfer '{transfer.Id.Value}': {release.Outcome}.");
        }

        record.Close(now, nameof(PaymentStatusResult.Rejected));
        process.Complete(now);
        await processRepository.SaveChangesAsync(cancellationToken);
        return ReconciliationStepOutcome.Rejected;
    }

    private async Task<ReconciliationStepOutcome> PersistStillUnknownAsync(
        ReconciliationRecord record,
        Transfer transfer,
        TransferProcessState process,
        DateTimeOffset now,
        ITransferProcessStateRepository processRepository,
        CancellationToken cancellationToken)
    {
        var nextAttemptCount = record.AttemptCount + 1;
        if (nextAttemptCount >= options.Value.EscalationAttemptThreshold)
        {
            transfer.RequireManualReview(now);
            record.EscalateToManualReview(now);
            process.MarkWaiting(now);
            await processRepository.SaveChangesAsync(cancellationToken);
            return ReconciliationStepOutcome.ManualReviewRequired;
        }

        var nextAttemptAt = now + TimeSpan.FromTicks(options.Value.RetryDelay.Ticks * nextAttemptCount);
        record.RecordUnknownAttempt(nextAttemptAt, nameof(PaymentStatusResult.Unknown), now);
        if (process.Status == TransferProcessStatus.Active
            && process.NextAction == TransferProcessAction.EnquirePaymentStatus)
        {
            process.RecordAttempt(nextAttemptAt, now);
        }

        await processRepository.SaveChangesAsync(cancellationToken);
        return ReconciliationStepOutcome.StillUnknown;
    }

    private async Task<ReconciliationStepOutcome> PersistEnquiryFailureAsync(
        ReconciliationClaim claim,
        string error,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var recordRepository = scope.ServiceProvider.GetRequiredService<IReconciliationRecordRepository>();
        var transferRepository = scope.ServiceProvider.GetRequiredService<ITransferRepository>();
        var processRepository = scope.ServiceProvider.GetRequiredService<ITransferProcessStateRepository>();
        var record = await recordRepository.GetByTransferIdAsync(claim.TransferId, cancellationToken);
        if (record is null
            || record.Id != claim.Id
            || record.Version != claim.Version
            || record.Status != ReconciliationStatus.Active)
        {
            return ReconciliationStepOutcome.LostClaim;
        }

        var transfer = await transferRepository.GetByIdAsync(claim.TransferId, cancellationToken)
            ?? throw new InvalidOperationException($"Transfer '{claim.TransferId.Value}' was not found.");
        var process = await processRepository.GetAsync(claim.TransferId, cancellationToken)
            ?? throw new InvalidOperationException($"Transfer process '{claim.TransferId.Value}' was not found.");

        if (transfer.State != TransferState.SubmissionStatusUnknown)
        {
            return ReconciliationStepOutcome.NotActionable;
        }

        var now = timeProvider.GetUtcNow();
        var nextAttemptCount = record.AttemptCount + 1;
        if (nextAttemptCount >= options.Value.EscalationAttemptThreshold)
        {
            transfer.RequireManualReview(now);
            record.EscalateToManualReview(now);
            process.MarkWaiting(now);
            await processRepository.SaveChangesAsync(cancellationToken);
            return ReconciliationStepOutcome.ManualReviewRequired;
        }

        var nextAttemptAt = now + TimeSpan.FromTicks(options.Value.RetryDelay.Ticks * nextAttemptCount);
        record.RecordEnquiryFailure(nextAttemptAt, error, now);
        if (process.Status == TransferProcessStatus.Active
            && process.NextAction == TransferProcessAction.EnquirePaymentStatus)
        {
            process.RecordAttempt(nextAttemptAt, now);
        }

        await processRepository.SaveChangesAsync(cancellationToken);
        return ReconciliationStepOutcome.EnquiryFailed;
    }
}
