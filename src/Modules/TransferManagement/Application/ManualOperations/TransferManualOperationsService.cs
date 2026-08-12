using TransferOrchestration.AccountBalance.Contracts;
using TransferOrchestration.AuditOperations.Contracts;
using TransferOrchestration.TransferManagement.Application.Persistence;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Application.Reconciliation;
using TransferOrchestration.TransferManagement.Contracts.ManualOperations;
using TransferOrchestration.TransferManagement.Domain.Transfers;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;

namespace TransferOrchestration.TransferManagement.Application.ManualOperations;

internal sealed class TransferManualOperationsService(
    ITransferRepository transferRepository,
    ITransferProcessStateRepository processRepository,
    IReconciliationRecordRepository reconciliationRepository,
    IAccountBalanceReservationFinalization reservationFinalization,
    ITransferManagementTransaction transaction,
    TransferManagementDbContext transferDbContext,
    IOperationsAuditWriter auditWriter,
    TimeProvider timeProvider) : ITransferManualOperations
{
    private const string RejectAction = "RejectFromManualReview";
    private const string ConfirmSettlementAction = "ConfirmSettlementFromManualReview";

    public Task<ManualTransferOperationResult> RejectFromManualReviewAsync(
        ManualTransferOperationCommand command,
        CancellationToken cancellationToken) =>
        ExecuteAsync(command, RejectAction, ApplyRejectAsync, cancellationToken);

    public Task<ManualTransferOperationResult> ConfirmSettlementFromManualReviewAsync(
        ManualTransferOperationCommand command,
        CancellationToken cancellationToken) =>
        ExecuteAsync(command, ConfirmSettlementAction, ApplyConfirmSettlementAsync, cancellationToken);

    private async Task<ManualTransferOperationResult> ExecuteAsync(
        ManualTransferOperationCommand command,
        string action,
        Func<Transfer, TransferProcessState, ReconciliationRecord?, DateTimeOffset, CancellationToken, Task<FinalizeFundsOutcome>> apply,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return new ManualTransferOperationResult(ManualTransferOperationOutcome.MissingReason);
        }

        var existing = await auditWriter.FindByCommandIdAsync(command.CommandId, cancellationToken);
        if (existing is not null)
        {
            return new ManualTransferOperationResult(
                ManualTransferOperationOutcome.Replay,
                existing.TransferId,
                existing.PreviousState,
                existing.NewState,
                existing.CorrelationId);
        }

        var transferId = new TransferId(command.TransferId);
        var transfer = await transferRepository.GetByIdAsync(transferId, cancellationToken);
        if (transfer is null)
        {
            return new ManualTransferOperationResult(ManualTransferOperationOutcome.TransferNotFound);
        }

        if (transfer.State != TransferState.ManualReviewRequired)
        {
            return new ManualTransferOperationResult(
                ManualTransferOperationOutcome.InvalidState,
                transfer.Id.Value,
                transfer.State.ToString(),
                transfer.State.ToString(),
                command.CorrelationId);
        }

        var previousState = transfer.State.ToString();
        var process = await processRepository.GetAsync(transferId, cancellationToken)
            ?? throw new InvalidOperationException($"Transfer process '{transferId.Value}' was not found.");
        var reconciliation = await reconciliationRepository.GetByTransferIdAsync(transferId, cancellationToken);
        var now = timeProvider.GetUtcNow();

        FinalizeFundsOutcome reservationOutcome;
        try
        {
            reservationOutcome = await apply(transfer, process, reconciliation, now, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return MapReservationFailure(transfer.Id.Value, previousState, transfer.State.ToString(), command.CorrelationId);
        }

        if (reservationOutcome is FinalizeFundsOutcome.ConflictingState
            or FinalizeFundsOutcome.ReservationNotFound
            or FinalizeFundsOutcome.AccountNotFound
            or FinalizeFundsOutcome.ContentionRetryExhausted)
        {
            return MapReservationFailure(transfer.Id.Value, previousState, transfer.State.ToString(), command.CorrelationId);
        }

        await transaction.ExecuteAsync(async ct =>
        {
            var dbTransaction = transferDbContext.Database.CurrentTransaction
                ?? throw new InvalidOperationException("Transfer management transaction is required.");
            auditWriter.Enlist(dbTransaction);
            auditWriter.Stage(new OperationsAuditEntry(
                command.CommandId,
                command.ActorId,
                action,
                transfer.Id.Value,
                previousState,
                transfer.State.ToString(),
                command.Reason.Trim(),
                command.CorrelationId,
                command.CausationId,
                now));
            await auditWriter.SaveStagedAsync(ct);
            await processRepository.SaveChangesAsync(ct);
        }, cancellationToken);

        return new ManualTransferOperationResult(
            ManualTransferOperationOutcome.Succeeded,
            transfer.Id.Value,
            previousState,
            transfer.State.ToString(),
            command.CorrelationId);
    }

    private static ManualTransferOperationResult MapReservationFailure(
        Guid transferId,
        string previousState,
        string currentState,
        Guid correlationId) =>
        new(
            ManualTransferOperationOutcome.ReservationConflict,
            transferId,
            previousState,
            currentState,
            correlationId);

    private async Task<FinalizeFundsOutcome> ApplyRejectAsync(
        Transfer transfer,
        TransferProcessState process,
        ReconciliationRecord? reconciliation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        transfer.RejectManually(now);

        var release = await reservationFinalization.ReleaseAsync(
            new FinalizeFundsRequest(transfer.Id.Value, transfer.SourceAccountId),
            cancellationToken);
        if (release.Outcome is not FinalizeFundsOutcome.Succeeded
            and not FinalizeFundsOutcome.AlreadyReleased)
        {
            throw new InvalidOperationException(
                $"Could not release reservation for transfer '{transfer.Id.Value}': {release.Outcome}.");
        }

        reconciliation?.CloseFromManualReview(now, "ManualReject");
        process.Complete(now);
        return release.Outcome;
    }

    private async Task<FinalizeFundsOutcome> ApplyConfirmSettlementAsync(
        Transfer transfer,
        TransferProcessState process,
        ReconciliationRecord? reconciliation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var consume = await reservationFinalization.ConsumeAsync(
            new FinalizeFundsRequest(transfer.Id.Value, transfer.SourceAccountId),
            cancellationToken);
        if (consume.Outcome is not FinalizeFundsOutcome.Succeeded
            and not FinalizeFundsOutcome.AlreadyConsumed)
        {
            throw new InvalidOperationException(
                $"Could not consume reservation for transfer '{transfer.Id.Value}': {consume.Outcome}.");
        }

        transfer.ConfirmSettlementManually(now);
        reconciliation?.CloseFromManualReview(now, "ManualConfirmSettlement");
        process.Complete(now);
        return consume.Outcome;
    }
}
