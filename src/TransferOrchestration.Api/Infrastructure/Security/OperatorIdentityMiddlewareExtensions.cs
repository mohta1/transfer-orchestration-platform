namespace TransferOrchestration.Api.Infrastructure.Security;

internal static class OperatorIdentityMiddlewareExtensions
{
    public static IApplicationBuilder UseOperatorIdentity(this IApplicationBuilder app) =>
        app.UseMiddleware<OperatorIdentityMiddleware>();
}
