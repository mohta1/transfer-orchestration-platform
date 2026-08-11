using Microsoft.Extensions.DependencyInjection;
using TransferOrchestration.PaymentNetwork.Contracts;
using TransferOrchestration.TransferManagement.Application.Persistence;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
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
    TimeProvider timeProvider) : IPaymentSubmissionProcessStep
{
    public async Task<PaymentSubmissionStepOutcome> ExecuteAsync(
        TransferId transferId,
        long claimedVersion,
        CancellationToken cancellationToken)
    {
        PaymentSubmissionRequest? request;
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
        try
        {
            result = await paymentNetworkGateway.SubmitAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Any provider uncertainty after preparation deliberately remains on the
            // persisted enquiry path. It is never converted into rejection or submit.
            return PaymentSubmissionStepOutcome.StatusUnknown;
        }

        return await PersistOutcomeAsync(transferId, request.Reference, result, cancellationToken);
    }

    private async Task<PaymentSubmissionStepOutcome> PersistOutcomeAsync(
        TransferId transferId,
        NetworkSubmissionReference reference,
        PaymentSubmissionResult result,
        CancellationToken cancellationToken)
    {
        await using var outcomeScope = scopeFactory.CreateAsyncScope();
        var transferRepository = outcomeScope.ServiceProvider.GetRequiredService<ITransferRepository>();
        var processRepository = outcomeScope.ServiceProvider.GetRequiredService<ITransferProcessStateRepository>();
        var transfer = await transferRepository.GetByIdAsync(transferId, cancellationToken)
            ?? throw new InvalidOperationException($"Transfer '{transferId.Value}' was not found.");
        var process = await processRepository.GetAsync(transferId, cancellationToken)
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
            PaymentSubmissionResult.Timeout => MarkUnknown(transfer, now),
            _ => throw new InvalidOperationException("Unsupported payment submission result.")
        };

        try
        {
            await processRepository.SaveChangesAsync(cancellationToken);
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

    private static PaymentSubmissionStepOutcome MarkUnknown(Transfer transfer, DateTimeOffset now)
    {
        transfer.MarkSubmissionStatusUnknown(now);
        return PaymentSubmissionStepOutcome.StatusUnknown;
    }
}
