using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TransferOrchestration.BuildingBlocks.Api;
using TransferOrchestration.BuildingBlocks.Security;
using TransferOrchestration.TransferManagement.Contracts.Queries;

namespace TransferOrchestration.AuditOperations.Api;

public static class OperationsEndpoints
{
    public static IEndpointRouteBuilder MapOperationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet("/api/operations/stuck-transfers", ListStuckTransfersAsync)
            .RequireAuthorization(AuthorizationPolicies.Operator);
        return endpoints;
    }

    private static async Task<IResult> ListStuckTransfersAsync(
        int? maxResults,
        IStuckTransferQueries queries,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await queries.ListAsync(new StuckTransferQueryRequest(maxResults), cancellationToken);
            return Results.Ok(result);
        }
        catch (StuckTransferQueryValidationException exception)
        {
            return ApiProblemResults.BadRequest("stuck_transfer_query_invalid", exception.Message);
        }
    }
}
