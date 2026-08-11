namespace TransferOrchestration.TransferManagement.Infrastructure.Outbox;

internal enum OutboxStatus
{
    Pending,
    Published,
    DeadLetter
}
