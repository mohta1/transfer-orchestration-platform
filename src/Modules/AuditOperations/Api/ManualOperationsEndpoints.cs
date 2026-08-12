using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TransferOrchestration.AuditOperations.Contracts;
using TransferOrchestration.TransferManagement.Contracts.ManualOperations;
using TransferOrchestration.TransferManagement.Api;

namespace TransferOrchestration.AuditOperations.Api;

public static class ManualOperationsEndpoints
{
    public static IEndpointRouteBuilder MapManualOperationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup("/api/transfers/{transferId:guid}/manual");
        group.MapPost("/reject", RejectAsync);
        group.MapPost("/confirm-settlement", ConfirmSettlementAsync);
        return endpoints;
    }

    private static async Task<IResult> RejectAsync(
        Guid transferId,
        ManualOperationHttpRequest request,
        HttpContext httpContext,
        ITransferManualOperations operations,
        ICorrelationContext correlationContext,
        IOperatorContext operatorContext,
        CancellationToken cancellationToken)
    {
        var validation = ValidateRequest(request, httpContext, operatorContext, out var commandId);
        if (validation is not null)
        {
            return validation;
        }

        var result = await operations.RejectFromManualReviewAsync(
            new ManualTransferOperationCommand(
                transferId,
                commandId!,
                operatorContext.OperatorId!,
                request.Reason!.Trim(),
                correlationContext.CorrelationId,
                correlationContext.CausationId),
            cancellationToken);

        return MapResult(result);
    }

    private static async Task<IResult> ConfirmSettlementAsync(
        Guid transferId,
        ManualOperationHttpRequest request,
        HttpContext httpContext,
        ITransferManualOperations operations,
        ICorrelationContext correlationContext,
        IOperatorContext operatorContext,
        CancellationToken cancellationToken)
    {
        var validation = ValidateRequest(request, httpContext, operatorContext, out var commandId);
        if (validation is not null)
        {
            return validation;
        }

        var result = await operations.ConfirmSettlementFromManualReviewAsync(
            new ManualTransferOperationCommand(
                transferId,
                commandId!,
                operatorContext.OperatorId!,
                request.Reason!.Trim(),
                correlationContext.CorrelationId,
                correlationContext.CausationId),
            cancellationToken);

        return MapResult(result);
    }

    private static IResult? ValidateRequest(
        ManualOperationHttpRequest request,
        HttpContext httpContext,
        IOperatorContext operatorContext,
        out string? commandId)
    {
        commandId = null;
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return ApiProblemResults.BadRequest(
                "manual_operation_reason_required",
                "Reason is required for manual operations.");
        }

        if (!httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var values)
            || values.Count != 1
            || string.IsNullOrWhiteSpace(values[0])
            || values[0]!.Length > 200)
        {
            return ApiProblemResults.BadRequest(
                "idempotency_key_invalid",
                "Idempotency-Key must contain one non-blank value of at most 200 characters.");
        }

        commandId = values[0]!;

        if (string.IsNullOrWhiteSpace(operatorContext.OperatorId))
        {
            return ApiProblemResults.BadRequest(
                "operator_id_required",
                "X-Operator-ID must contain one non-blank operator identity.");
        }

        return null;
    }

    private static IResult MapResult(ManualTransferOperationResult result) =>
        result.Outcome switch
        {
            ManualTransferOperationOutcome.Succeeded or ManualTransferOperationOutcome.Replay =>
                Results.Ok(new ManualOperationHttpResponse(
                    result.TransferId,
                    result.PreviousState,
                    result.NewState,
                    result.CorrelationId,
                    result.Outcome.ToString())),
            ManualTransferOperationOutcome.MissingReason =>
                ApiProblemResults.BadRequest(
                    "manual_operation_reason_required",
                    "Reason is required for manual operations."),
            ManualTransferOperationOutcome.InvalidState =>
                ApiProblemResults.Conflict(
                    "manual_operation_invalid_state",
                    "Manual operation is not permitted from the current transfer state."),
            ManualTransferOperationOutcome.TransferNotFound =>
                ApiProblemResults.NotFound(
                    "transfer_not_found",
                    "Transfer was not found."),
            ManualTransferOperationOutcome.ReservationConflict or ManualTransferOperationOutcome.ContentionRetryExhausted =>
                ApiProblemResults.Conflict(
                    "reservation_conflict",
                    "Reservation could not be finalized for this manual operation."),
            _ => throw new InvalidOperationException($"Unsupported manual operation outcome '{result.Outcome}'.")
        };

    internal sealed record ManualOperationHttpRequest(string? Reason);

    internal sealed record ManualOperationHttpResponse(
        Guid? TransferId,
        string? PreviousState,
        string? NewState,
        Guid? CorrelationId,
        string Outcome);
}
