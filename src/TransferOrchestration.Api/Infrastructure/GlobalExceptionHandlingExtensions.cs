using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using TransferOrchestration.BuildingBlocks.Api;

namespace TransferOrchestration.Api.Infrastructure;

internal static class GlobalExceptionHandlingExtensions
{
    public static IApplicationBuilder UseSafeExceptionHandling(this IApplicationBuilder app)
    {
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var feature = context.Features.Get<IExceptionHandlerFeature>();
                if (feature?.Error is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
                {
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsync(
                    JsonSerializer.Serialize(ApiProblemResults.InternalErrorDetails(), ApiProblemResults.JsonOptions));
            });
        });

        return app;
    }
}
