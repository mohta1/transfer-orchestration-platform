using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence.Idempotency;

namespace TransferOrchestration.TransferManagement.Infrastructure.Persistence.Configurations;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_records", table =>
        {
            table.HasCheckConstraint(
                "ck_idempotency_records_completion",
                "(status = 'Processing' AND completed_at_utc IS NULL) OR " +
                "(status = 'Completed' AND transfer_id IS NOT NULL AND completed_at_utc IS NOT NULL)");
        });
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(record => record.Scope).HasColumnName("scope").HasMaxLength(64);
        builder.Property(record => record.Key).HasColumnName("idempotency_key").HasMaxLength(200);
        builder.Property(record => record.Fingerprint).HasColumnName("fingerprint").HasMaxLength(64).IsFixedLength();
        builder.Property(record => record.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
        builder.Property(record => record.TransferId).HasColumnName("transfer_id");
        builder.Property(record => record.ResultOutcome).HasColumnName("result_outcome").HasMaxLength(32);
        builder.Property(record => record.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(record => record.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.HasIndex(record => new { record.Scope, record.Key })
            .IsUnique()
            .HasDatabaseName("ux_idempotency_records_scope_key");
    }
}
