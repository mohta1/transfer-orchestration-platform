namespace TransferOrchestration.TransferManagement.Application.ProcessManagement;

internal enum TransferProcessAction
{
    None,
    ContinueWorkflow,
    RequestFraudScreening,
    ReserveBalance,
    SubmitToPaymentNetwork,
    EnquirePaymentStatus,
    ReleaseReservation
}
