namespace TransferOrchestration.TransferManagement.Application.ProcessManagement;

internal sealed class TransferProcessConcurrencyConflictException : Exception
{
    public TransferProcessConcurrencyConflictException(Guid transferId)
        : base($"Transfer process '{transferId}' is no longer owned by this worker.")
    {
        TransferId = transferId;
    }

    public TransferProcessConcurrencyConflictException(Guid transferId, Exception innerException)
        : base($"Transfer process '{transferId}' was changed by another operation.", innerException)
    {
        TransferId = transferId;
    }

    public Guid TransferId { get; }
}
