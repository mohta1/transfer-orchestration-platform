namespace TransferOrchestration.TransferManagement.Domain.Transfers;

internal enum TransferState
{
    Draft = 1,
    Submitted = 2,
    ValidationFailed = 3,
    PendingAuthorisation = 4,
    Authorised = 5,
    PendingFraudScreening = 6,
    FraudRejected = 7,
    PendingBalanceReservation = 8,
    BalanceReserved = 9,
    PendingExternalSubmission = 10,
    SubmissionStatusUnknown = 11,
    SettlementPending = 12,
    Completed = 13,
    Rejected = 14,
    Cancelled = 15,
    CompensationRequired = 16,
    ManualReviewRequired = 17
}
