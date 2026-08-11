using Microsoft.EntityFrameworkCore;
using TransferOrchestration.AccountBalance.Application.Persistence;
using TransferOrchestration.AccountBalance.Domain.Accounts;

namespace TransferOrchestration.AccountBalance.Infrastructure.Persistence.Repositories;

internal sealed class AccountRepository(AccountBalanceDbContext dbContext)
    : IAccountRepository
{
    public Task<Account?> GetByIdAsync(
        AccountId accountId,
        CancellationToken cancellationToken) =>
        dbContext.Accounts
            .Include(account => account.Reservations)
            .SingleOrDefaultAsync(account => account.Id == accountId, cancellationToken);

    public async Task AddAsync(
        Account account,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        await dbContext.Accounts.AddAsync(account, cancellationToken);
    }

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

            throw new AccountConcurrencyConflictException(accountId, exception);
        }
    }
}
