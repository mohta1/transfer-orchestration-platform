using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TransferOrchestration.BuildingBlocks.Api;
using TransferOrchestration.TransferManagement.Contracts.Queries;

namespace TransferOrchestration.TransferManagement.Api;

public static class TransferReadEndpoints
{
    public static IEndpointRouteBuilder MapTransferReadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet("/api/transfers/{transferId:guid}", GetByIdAsync);
        return endpoints;
    }

    private static async Task<IResult> GetByIdAsync(
        Guid transferId,
        ITransferQueries queries,
        CancellationToken cancellationToken)
    {
        var transfer = await queries.GetByIdAsync(transferId, cancellationToken);
        if (transfer is null)
        {
            return ApiProblemResults.NotFound(
                "transfer_not_found",
                "Transfer was not found.");
        }

        return Results.Ok(transfer);
    }
}
