namespace TransferOrchestration.TransferManagement.Application.ProcessManagement;

internal enum TransferProcessStep
{
    Created,
    ActionScheduled,
    WaitingForOutcome,
    Completed
}
