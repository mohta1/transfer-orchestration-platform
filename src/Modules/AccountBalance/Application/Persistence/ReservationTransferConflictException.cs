namespace TransferOrchestration.AccountBalance.Application.Persistence;

internal sealed class ReservationTransferConflictException(Exception innerException)
    : Exception("A reservation already exists for this transfer.", innerException);
