namespace TransferOrchestration.TransferManagement.Application.Persistence;

internal sealed class TransferConcurrencyConflictException : Exception
{
    public TransferConcurrencyConflictException(Guid transferId, Exception innerException)
        : base($"Transfer '{transferId}' was changed by another operation. Reload it before retrying the workflow transition.", innerException)
    {
        TransferId = transferId;
    }

    public Guid TransferId { get; }
}
