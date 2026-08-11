using Microsoft.EntityFrameworkCore;
using TransferOrchestration.AccountBalance.Domain.Accounts;

namespace TransferOrchestration.AccountBalance.Infrastructure.Persistence;

public sealed class AccountBalanceDbContext(
    DbContextOptions<AccountBalanceDbContext> options)
    : DbContext(options)
{
    public const string Schema = "account_balance";

    internal DbSet<Account> Accounts => Set<Account>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AccountBalanceDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
