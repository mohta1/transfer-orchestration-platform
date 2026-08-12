using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace TransferOrchestration.TransferManagement.Api;

public static class ApiProblemResults
{
    private const string ErrorTypePrefix = "https://transfer-orchestration/errors/";

    public static IResult BadRequest(string code, string detail) =>
        Problem(StatusCodes.Status400BadRequest, code, "Bad request", detail);

    public static IResult NotFound(string code, string detail) =>
        Problem(StatusCodes.Status404NotFound, code, "Resource not found", detail);

    public static IResult Conflict(string code, string detail) =>
        Problem(StatusCodes.Status409Conflict, code, "Conflict", detail);

    public static IResult ValidationFailed(IReadOnlyList<string> errors) =>
        Problem(
            StatusCodes.Status400BadRequest,
            "validation_failed",
            "Validation failed",
            "One or more request fields are invalid.",
            new Dictionary<string, object?> { ["errors"] = errors });

    public static IResult InternalError() =>
        Problem(
            StatusCodes.Status500InternalServerError,
            "internal_error",
            "Internal server error",
            "An unexpected error occurred while processing the request.");

    private static IResult Problem(
        int statusCode,
        string code,
        string title,
        string detail,
        IDictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Type = $"{ErrorTypePrefix}{code}"
        };

        problem.Extensions["code"] = code;
        if (extensions is not null)
        {
            foreach (var entry in extensions)
            {
                problem.Extensions[entry.Key] = entry.Value;
            }
        }

        return Results.Json(problem, statusCode: statusCode, contentType: "application/problem+json");
    }
}
