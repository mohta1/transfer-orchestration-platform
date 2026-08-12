using System.Security.Claims;
using TransferOrchestration.BuildingBlocks.Security;

namespace TransferOrchestration.Api.Infrastructure.Security;

internal sealed class HttpCallerIdentity(IHttpContextAccessor httpContextAccessor) : ICallerIdentity
{
    public bool IsAuthenticated =>
        httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public string? SubjectId =>
        httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? httpContextAccessor.HttpContext?.User.FindFirstValue("sub");

    public Guid? AccountId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User.FindFirstValue(SecurityClaimTypes.AccountId);
            return Guid.TryParse(value, out var accountId) && accountId != Guid.Empty
                ? accountId
                : null;
        }
    }

    public bool IsCustomer =>
        httpContextAccessor.HttpContext?.User.IsInRole(SecurityClaimTypes.CustomerRole) == true;

    public bool IsOperator =>
        httpContextAccessor.HttpContext?.User.IsInRole(SecurityClaimTypes.OperatorRole) == true;
}
