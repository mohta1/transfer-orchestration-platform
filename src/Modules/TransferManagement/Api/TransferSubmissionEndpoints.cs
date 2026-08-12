using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TransferOrchestration.AuditOperations.Contracts;
using TransferOrchestration.BuildingBlocks.Api;
using TransferOrchestration.TransferManagement.Application.Submission;

using TransferOrchestration.BuildingBlocks.Security;

namespace TransferOrchestration.TransferManagement.Api;

public static class TransferSubmissionEndpoints
{
    public static IEndpointRouteBuilder MapTransferSubmissionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost("/api/transfers", SubmitAsync)
            .RequireAuthorization(AuthorizationPolicies.Customer);
        return endpoints;
    }

    private static async Task<IResult> SubmitAsync(
        TransferSubmissionHttpRequest request,
        HttpContext httpContext,
        ITransferSubmissionService service,
        ICorrelationContext correlationContext,
        CancellationToken cancellationToken)
    {
        if (!httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var values)
            || values.Count != 1
            || string.IsNullOrWhiteSpace(values[0])
            || values[0]!.Length > 200)
        {
            return ApiProblemResults.BadRequest(
                "idempotency_key_invalid",
                "Idempotency-Key must contain one non-blank value of at most 200 characters.");
        }

        if (httpContext.Request.Headers.TryGetValue("X-Correlation-ID", out var correlationValues))
        {
            if (correlationValues.Count != 1
                || !Guid.TryParse(correlationValues[0], out var suppliedCorrelationId)
                || suppliedCorrelationId == Guid.Empty)
            {
                return ApiProblemResults.BadRequest(
                    "correlation_id_invalid",
                    "X-Correlation-ID must be a non-empty GUID.");
            }

            correlationContext.SetCorrelationId(suppliedCorrelationId);
        }

        var correlationId = correlationContext.CorrelationId;
        httpContext.Response.Headers["X-Correlation-ID"] = correlationId.ToString("D");

        var result = await service.SubmitAsync(
            new SubmitTransferCommand(
                request.SourceAccountId,
                request.DestinationAccountId,
                request.Amount,
                request.Currency,
                request.TransferType,
                values[0]!,
                correlationId),
            cancellationToken);

        var response = new TransferSubmissionHttpResponse(
            result.TransferId,
            result.CorrelationId,
            result.State?.ToString(),
            result.Outcome.ToString());
        return result.Outcome switch
        {
            TransferSubmissionOutcome.Accepted or TransferSubmissionOutcome.Replay => Results.Accepted(value: response),
            TransferSubmissionOutcome.Processing => Results.Accepted(value: response),
            TransferSubmissionOutcome.Conflict => ApiProblemResults.Conflict(
                "idempotency_conflict",
                "Idempotency-Key was already used with a different semantic request."),
            TransferSubmissionOutcome.ValidationFailed => ApiProblemResults.ValidationFailed(result.Errors ?? []),
            TransferSubmissionOutcome.AuthorizationRejected => ApiProblemResults.Forbidden(
                "authorization_rejected",
                "Customer is not authorized to use the source account."),
            TransferSubmissionOutcome.DailyLimitExceeded => ApiProblemResults.UnprocessableEntity(
                "daily_limit_exceeded",
                "Transfer exceeds the daily transfer limit."),
            TransferSubmissionOutcome.FraudRejected => ApiProblemResults.UnprocessableEntity(
                "fraud_rejected",
                "Transfer was rejected by fraud screening."),
            _ => throw new InvalidOperationException($"Unsupported submission outcome '{result.Outcome}'.")
        };
    }

    internal sealed record TransferSubmissionHttpRequest(
        Guid SourceAccountId,
        Guid DestinationAccountId,
        decimal Amount,
        string? Currency,
        string? TransferType);

    internal sealed record TransferSubmissionHttpResponse(
        Guid? TransferId,
        Guid? CorrelationId,
        string? State,
        string Outcome);
}
