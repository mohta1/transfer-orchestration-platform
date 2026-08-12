using Microsoft.EntityFrameworkCore;
using TransferOrchestration.AuditOperations.Domain;

namespace TransferOrchestration.AuditOperations.Infrastructure.Persistence;

public sealed class AuditOperationsDbContext(DbContextOptions<AuditOperationsDbContext> options) : DbContext(options)
{
    public const string Schema = "audit_operations";

    internal DbSet<OperationsAuditRecord> OperationsAuditRecords => Set<OperationsAuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuditOperationsDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
