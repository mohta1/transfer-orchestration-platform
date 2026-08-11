using Microsoft.EntityFrameworkCore;
using Npgsql;
using TransferOrchestration.AccountBalance.Application.Persistence;
using TransferOrchestration.AccountBalance.Domain.Accounts;

namespace TransferOrchestration.AccountBalance.Infrastructure.Persistence.Repositories;

internal sealed class AccountRepository(AccountBalanceDbContext dbContext)
    : IAccountRepository
{
    public Task<Account?> GetByIdAsync(
        AccountId accountId,
        Guid reservationTransferId,
        CancellationToken cancellationToken) =>
        dbContext.Accounts
            .Include(account => account.Reservations.Where(
                reservation => reservation.TransferId == reservationTransferId))
            .SingleOrDefaultAsync(account => account.Id == accountId, cancellationToken);

    public async Task AddAsync(
        Account account,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        await dbContext.Accounts.AddAsync(account, cancellationToken);
    }

    public Task<ReservationIntent?> GetReservationIntentAsync(
        Guid transferId,
        CancellationToken cancellationToken) =>
        dbContext.Set<BalanceReservation>()
            .AsNoTracking()
            .Where(reservation => reservation.TransferId == transferId)
            .Select(reservation => new ReservationIntent(
                EF.Property<AccountId>(reservation, "account_id").Value,
                reservation.Amount))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            var accountId = exception.Entries
                .Where(entry => entry.Entity is Account)
                .Select(entry => ((Account)entry.Entity).Id.Value)
                .FirstOrDefault();

            dbContext.ChangeTracker.Clear();

            throw new AccountConcurrencyConflictException(accountId, exception);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "ux_balance_reservations_transfer_id"
            })
        {
            dbContext.ChangeTracker.Clear();
            throw new ReservationTransferConflictException(exception);
        }
    }
}
