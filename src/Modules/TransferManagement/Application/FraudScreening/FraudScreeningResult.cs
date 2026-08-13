namespace TransferOrchestration.TransferManagement.Application.FraudScreening;

internal enum FraudScreeningResult
{
    Approved,
    Rejected,
    ManualReviewRequired,
    Timeout,
    TemporarilyUnavailable
}
