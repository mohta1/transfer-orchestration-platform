using Microsoft.Extensions.Options;
using TransferOrchestration.TransferManagement.Application.Idempotency;
using TransferOrchestration.TransferManagement.Application.Submission;

namespace TransferOrchestration.TransferManagement.Infrastructure.Submission;

internal sealed class AllowCustomerAuthorization : ICustomerAuthorization
{
    public Task<DecisionOutcome> IsAuthorizedAsync(Guid sourceAccountId, CancellationToken cancellationToken) =>
        Task.FromResult(DecisionOutcome.Approved);
}

internal sealed class ConfiguredDailyTransferLimit(IOptions<SubmissionPolicyOptions> options) : IDailyTransferLimit
{
    private readonly decimal _maximum = options.Value.MaximumDailyTransferAmount > 0
        ? options.Value.MaximumDailyTransferAmount
        : throw new InvalidOperationException("TransferSubmission:MaximumDailyTransferAmount must be greater than zero.");

    public DecisionOutcome Evaluate(decimal amount, string currency) =>
        amount <= _maximum ? DecisionOutcome.Approved : DecisionOutcome.Rejected;
}

internal sealed class AllowFraudScreening : IFraudScreening
{
    public Task<DecisionOutcome> ScreenAsync(TransferSubmissionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(DecisionOutcome.Approved);
}
