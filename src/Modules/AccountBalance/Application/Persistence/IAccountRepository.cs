using TransferOrchestration.AccountBalance.Domain.Accounts;

namespace TransferOrchestration.AccountBalance.Application.Persistence;

internal interface IAccountRepository
{
    Task<Account?> GetByIdAsync(
        AccountId accountId,
        Guid reservationTransferId,
        CancellationToken cancellationToken);

    Task<ReservationIntent?> GetReservationIntentAsync(
        Guid transferId,
        CancellationToken cancellationToken);

    Task AddAsync(
        Account account,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

internal sealed record ReservationIntent(Guid AccountId, decimal Amount);
