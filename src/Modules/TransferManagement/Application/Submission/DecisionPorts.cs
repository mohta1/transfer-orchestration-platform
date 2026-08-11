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
    DecisionOutcome Evaluate(decimal amount, string currency);
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
