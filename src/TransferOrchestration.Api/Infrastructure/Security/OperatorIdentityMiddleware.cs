using TransferOrchestration.AuditOperations.Contracts;
using TransferOrchestration.BuildingBlocks.Security;

namespace TransferOrchestration.Api.Infrastructure.Security;

internal sealed class OperatorIdentityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext httpContext,
        ICallerIdentity callerIdentity,
        IOperatorContext operatorContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(callerIdentity);
        ArgumentNullException.ThrowIfNull(operatorContext);

        if (callerIdentity.IsAuthenticated
            && callerIdentity.IsOperator
            && !string.IsNullOrWhiteSpace(callerIdentity.SubjectId))
        {
            operatorContext.SetOperatorId(callerIdentity.SubjectId);
        }

        await next(httpContext);
    }
}
