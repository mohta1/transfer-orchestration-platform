using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace TransferOrchestration.Api.Infrastructure;

internal static class GlobalExceptionHandlingExtensions
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

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
                var problem = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "Internal server error",
                    Detail = "An unexpected error occurred while processing the request.",
                    Type = "https://transfer-orchestration/errors/internal_error",
                    Extensions = { ["code"] = "internal_error" }
                };
                await context.Response.WriteAsync(JsonSerializer.Serialize(problem, SerializerOptions));
            });
        });

        return app;
    }
}
