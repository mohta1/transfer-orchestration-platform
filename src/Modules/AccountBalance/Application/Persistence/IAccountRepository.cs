using TransferOrchestration.AccountBalance.Domain.Accounts;

namespace TransferOrchestration.AccountBalance.Application.Persistence;

internal interface IAccountRepository
{
    Task<Account?> GetByIdAsync(
        AccountId accountId,
        CancellationToken cancellationToken);

    Task AddAsync(
        Account account,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
