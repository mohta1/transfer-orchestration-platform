using TransferOrchestration.BuildingBlocks.Security;
using TransferOrchestration.TransferManagement.Application.Submission;

namespace TransferOrchestration.TransferManagement.Infrastructure.Submission;

internal sealed class AuthenticatedCustomerAuthorization(ICallerIdentity callerIdentity) : ICustomerAuthorization
{
    public Task<DecisionOutcome> IsAuthorizedAsync(Guid sourceAccountId, CancellationToken cancellationToken) =>
        Task.FromResult(
            callerIdentity.AccountId == sourceAccountId
                ? DecisionOutcome.Approved
                : DecisionOutcome.Rejected);
}
