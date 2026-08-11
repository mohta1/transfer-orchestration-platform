using Microsoft.EntityFrameworkCore;
using TransferOrchestration.TransferManagement.Domain.Transfers;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence.Idempotency;

namespace TransferOrchestration.TransferManagement.Infrastructure.Persistence;

public sealed class TransferManagementDbContext(
    DbContextOptions<TransferManagementDbContext> options)
    : DbContext(options)
{
    public const string Schema = "transfer_management";

    internal DbSet<Transfer> Transfers => Set<Transfer>();

    internal DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(TransferManagementDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
