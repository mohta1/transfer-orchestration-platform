using Microsoft.EntityFrameworkCore;

namespace TransferOrchestration.AccountBalance.Infrastructure.Persistence;

public sealed class AccountBalanceDbContext(
    DbContextOptions<AccountBalanceDbContext> options)
    : DbContext(options)
{
    public const string Schema = "account_balance";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AccountBalanceDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
