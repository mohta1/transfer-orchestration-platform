using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using TransferOrchestration.BuildingBlocks.Api;

namespace TransferOrchestration.Api.Infrastructure.Security;

internal sealed class ApiAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (authorizeResult.Challenged)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(
                    ApiProblemResults.CreateProblemDetails(
                        StatusCodes.Status401Unauthorized,
                        "unauthorized",
                        "Unauthorized",
                        "Authentication is required."),
                    ApiProblemResults.JsonOptions));
            return;
        }

        if (authorizeResult.Forbidden)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(
                    ApiProblemResults.CreateProblemDetails(
                        StatusCodes.Status403Forbidden,
                        "forbidden",
                        "Forbidden",
                        "The authenticated caller is not authorized for this operation."),
                    ApiProblemResults.JsonOptions));
            return;
        }

        await _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}
