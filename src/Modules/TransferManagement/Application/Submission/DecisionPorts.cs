using TransferOrchestration.TransferManagement.Application.Idempotency;

namespace TransferOrchestration.TransferManagement.Application.Submission;

internal enum DecisionOutcome
{
    Approved,
    Rejected
}

internal interface ICustomerAuthorization
{
    Task<DecisionOutcome> IsAuthorizedAsync(Guid sourceAccountId, CancellationToken cancellationToken);
}

internal interface IDailyTransferLimit
{
    Task<DecisionOutcome> TryConsumeAsync(Guid sourceAccountId, decimal amount, string currency, DateOnly utcDay, CancellationToken cancellationToken);
}

internal interface IFraudScreening
{
    Task<DecisionOutcome> ScreenAsync(TransferSubmissionRequest request, CancellationToken cancellationToken);
}

internal sealed class SubmissionPolicyOptions
{
    public const string SectionName = "TransferSubmission";

    public decimal MaximumDailyTransferAmount { get; set; }
}
