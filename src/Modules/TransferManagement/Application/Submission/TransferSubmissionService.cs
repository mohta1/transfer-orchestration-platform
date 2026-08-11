using TransferOrchestration.BuildingBlocks.Domain;
using TransferOrchestration.TransferManagement.Application.Idempotency;
using TransferOrchestration.TransferManagement.Application.Persistence;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.TransferManagement.Application.Submission;

internal sealed class TransferSubmissionService(
    ITransferSubmissionIdempotencyStore idempotencyStore,
    ITransferRepository transferRepository,
    ITransferProcessStateRepository processRepository,
    ITransferProcessManager processManager,
    ITransferManagementTransaction transaction,
    ICustomerAuthorization customerAuthorization,
    IDailyTransferLimit dailyTransferLimit,
    IFraudScreening fraudScreening,
    TimeProvider timeProvider) : ITransferSubmissionService
{
    public async Task<SubmitTransferResult> SubmitAsync(SubmitTransferCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = Validate(command);
        if (validation.Errors.Count != 0)
        {
            return new SubmitTransferResult(TransferSubmissionOutcome.ValidationFailed, Errors: validation.Errors);
        }

        var request = new TransferSubmissionRequest(
            command.SourceAccountId,
            command.DestinationAccountId,
            command.Amount,
            validation.Currency!,
            validation.Type!.Value);
        var now = timeProvider.GetUtcNow();
        IdempotencyClaim? claim = null;
        Transfer? transfer = null;
        await transaction.ExecuteAsync(async token =>
        {
            claim = await idempotencyStore.TryClaimAsync(command.IdempotencyKey, TransferSubmissionFingerprint.Create(request), now, token);
            if (claim.Outcome != IdempotencyClaimOutcome.Owner) return;

            transfer = Transfer.Create(request.SourceAccountId, request.DestinationAccountId, request.Amount, request.Currency, request.Type, now);
            transfer.Submit(now);
            transfer.RequestAuthorisation(now);
            await processManager.CreateWithTransferAsync(transfer, command.CorrelationId, now, token);
            await idempotencyStore.LinkToTransferAsync(claim.OwnerToken!.Value, transfer.Id.Value, token);
        }, cancellationToken);

        if (claim is null)
        {
            throw new InvalidOperationException("Idempotency claim was not evaluated.");
        }

        if (claim.Outcome == IdempotencyClaimOutcome.Conflict)
        {
            return new SubmitTransferResult(TransferSubmissionOutcome.Conflict);
        }

        if (claim.Outcome == IdempotencyClaimOutcome.Processing)
        {
            return new SubmitTransferResult(TransferSubmissionOutcome.Processing);
        }

        if (claim.Outcome == IdempotencyClaimOutcome.Completed)
        {
            return await ReplayAsync(claim.Result!, cancellationToken);
        }

        if (transfer is null)
        {
            throw new InvalidOperationException("The claim owner has no durable Transfer.");
        }

        var outcome = TransferSubmissionOutcome.Accepted;
        if (await customerAuthorization.IsAuthorizedAsync(request.SourceAccountId, cancellationToken) == DecisionOutcome.Rejected)
        {
            transfer.RejectAuthorisation(now);
            outcome = TransferSubmissionOutcome.AuthorizationRejected;
        }
        else
        {
            transfer.Authorise(now);
            if (await dailyTransferLimit.TryConsumeAsync(request.SourceAccountId, request.Amount, request.Currency, DateOnly.FromDateTime(now.UtcDateTime), cancellationToken) == DecisionOutcome.Rejected)
            {
                transfer.RejectDailyLimit(now);
                outcome = TransferSubmissionOutcome.DailyLimitExceeded;
            }
            else
            {
                transfer.BeginFraudScreening(now);
                if (await fraudScreening.ScreenAsync(request, cancellationToken) == DecisionOutcome.Rejected)
                {
                    transfer.RejectForFraud(now);
                    outcome = TransferSubmissionOutcome.FraudRejected;
                }
                else
                {
                    transfer.RequestBalanceReservation(now);
                }
            }
        }

        await transaction.ExecuteAsync(async token =>
        {
            if (outcome == TransferSubmissionOutcome.Accepted)
            {
                await processManager.ScheduleAsync(transfer.Id, TransferProcessAction.ReserveBalance, now, now, token);
            }
            else
            {
                await processManager.CompleteAsync(transfer.Id, now, token);
            }

            await idempotencyStore.CompleteAsync(
                claim.OwnerToken!.Value,
                new TransferSubmissionResult(transfer.Id.Value, outcome.ToString()),
                now,
                token);
        }, cancellationToken);

        return new SubmitTransferResult(outcome, transfer.Id.Value, command.CorrelationId, transfer.State);
    }

    private async Task<SubmitTransferResult> ReplayAsync(TransferSubmissionResult result, CancellationToken cancellationToken)
    {
        var transfer = await transferRepository.GetByIdAsync(new TransferId(result.TransferId), cancellationToken)
            ?? throw new InvalidOperationException("A completed idempotency result has no Transfer.");
        var process = await processRepository.GetAsync(transfer.Id, cancellationToken)
            ?? throw new InvalidOperationException("A completed idempotency result has no ProcessState.");
        return new SubmitTransferResult(
            MapReplayOutcome(result.Outcome, transfer.State),
            transfer.Id.Value,
            process.CorrelationId,
            transfer.State);
    }

    private static TransferSubmissionOutcome MapReplayOutcome(string? outcome, TransferState state)
    {
        if (Enum.TryParse<TransferSubmissionOutcome>(outcome, out var parsed))
        {
            return parsed == TransferSubmissionOutcome.Accepted ? TransferSubmissionOutcome.Replay : parsed;
        }

        return state switch
        {
            TransferState.PendingBalanceReservation => TransferSubmissionOutcome.Replay,
            TransferState.FraudRejected => TransferSubmissionOutcome.FraudRejected,
            TransferState.Rejected => TransferSubmissionOutcome.Replay,
            _ => throw new InvalidOperationException($"Transfer state '{state}' is not a completed TASK-06 submission result.")
        };
    }

    private static ValidationResult Validate(SubmitTransferCommand command)
    {
        var errors = new List<string>();
        if (command.SourceAccountId == Guid.Empty) errors.Add("SourceAccountId is required.");
        if (command.DestinationAccountId == Guid.Empty) errors.Add("DestinationAccountId is required.");
        if (command.SourceAccountId != Guid.Empty && command.SourceAccountId == command.DestinationAccountId) errors.Add("SourceAccountId and DestinationAccountId must differ.");
        if (command.Amount <= 0) errors.Add("Amount must be greater than zero.");
        try { MonetaryAmountGuard.EnsureRepresentable(command.Amount, "Amount"); }
        catch (DomainException exception) { errors.Add(exception.Message); }

        var currency = command.Currency?.Trim().ToUpperInvariant();
        if (currency is null || currency.Length != 3 || currency.Any(character => character is < 'A' or > 'Z'))
        {
            errors.Add("Currency must be a three-letter alphabetic code.");
        }

        TransferType? type = null;
        if (!string.Equals(command.TransferType, nameof(TransferType.InternalBank), StringComparison.OrdinalIgnoreCase)
            && !string.Equals(command.TransferType, nameof(TransferType.DomesticInterbank), StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("TransferType must be InternalBank or DomesticInterbank.");
        }
        else
        {
            type = Enum.Parse<TransferType>(command.TransferType!, true);
        }

        return new ValidationResult(errors, currency, type);
    }

    private sealed record ValidationResult(IReadOnlyList<string> Errors, string? Currency, TransferType? Type);
}
