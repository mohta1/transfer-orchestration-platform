using Microsoft.AspNetCore.Builder;
using TransferOrchestration.AuditOperations.Infrastructure.Correlation;

namespace TransferOrchestration.AuditOperations.Infrastructure;

public static class CorrelationMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationContext(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<CorrelationMiddleware>();
    }
}
