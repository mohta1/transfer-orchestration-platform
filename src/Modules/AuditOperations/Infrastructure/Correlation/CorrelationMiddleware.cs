using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TransferOrchestration.AuditOperations.Contracts;

namespace TransferOrchestration.AuditOperations.Infrastructure.Correlation;

internal sealed class CorrelationMiddleware(
    RequestDelegate next,
    ILogger<CorrelationMiddleware> logger)
{
    private static readonly Action<ILogger, string, string, Guid, Guid?, Guid?, string?, Exception?> RequestCompleted =
        LoggerMessage.Define<string, string, Guid, Guid?, Guid?, string?>(
            LogLevel.Information,
            new EventId(1, "RequestCorrelated"),
            "Handled {Method} {Path} with CorrelationId {CorrelationId} CausationId {CausationId} TransferId {TransferId} OperatorId {OperatorId}");

    public async Task InvokeAsync(
        HttpContext httpContext,
        ICorrelationContext correlationContext,
        IOperatorContext operatorContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(correlationContext);
        ArgumentNullException.ThrowIfNull(operatorContext);

        var correlationId = ResolveCorrelationId(httpContext);
        correlationContext.SetCorrelationId(correlationId);
        httpContext.Response.Headers["X-Correlation-ID"] = correlationId.ToString("D");
        httpContext.Items["CorrelationId"] = correlationId;

        if (httpContext.Request.Headers.TryGetValue("X-Causation-ID", out var causationValues)
            && causationValues.Count == 1
            && Guid.TryParse(causationValues[0], out var causationId)
            && causationId != Guid.Empty)
        {
            correlationContext.SetCausationId(causationId);
        }

        if (httpContext.Request.Headers.TryGetValue("X-Operator-ID", out var operatorValues)
            && operatorValues.Count == 1
            && !string.IsNullOrWhiteSpace(operatorValues[0]))
        {
            operatorContext.SetOperatorId(operatorValues[0]!);
        }

        var transferId = ResolveTransferId(httpContext);
        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["CausationId"] = correlationContext.CausationId,
            ["TransferId"] = transferId,
            ["OperatorId"] = operatorContext.OperatorId
        }))
        {
            await next(httpContext);
            RequestCompleted(
                logger,
                httpContext.Request.Method,
                httpContext.Request.Path,
                correlationId,
                correlationContext.CausationId,
                transferId,
                operatorContext.OperatorId,
                null);
        }
    }

    private static Guid ResolveCorrelationId(HttpContext httpContext)
    {
        if (httpContext.Request.Headers.TryGetValue("X-Correlation-ID", out var values)
            && values.Count == 1
            && Guid.TryParse(values[0], out var correlationId)
            && correlationId != Guid.Empty)
        {
            return correlationId;
        }

        return Guid.NewGuid();
    }

    private static Guid? ResolveTransferId(HttpContext httpContext)
    {
        if (httpContext.Request.RouteValues.TryGetValue("transferId", out var routeValue)
            && routeValue is string routeText
            && Guid.TryParse(routeText, out var transferId)
            && transferId != Guid.Empty)
        {
            return transferId;
        }

        return null;
    }
}
