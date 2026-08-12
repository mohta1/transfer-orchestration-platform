using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransferOrchestration.AuditOperations.Domain;

namespace TransferOrchestration.AuditOperations.Infrastructure.Persistence.Configurations;

internal sealed class OperationsAuditRecordConfiguration : IEntityTypeConfiguration<OperationsAuditRecord>
{
    public void Configure(EntityTypeBuilder<OperationsAuditRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("operations_audit_records");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(record => record.CommandId).HasColumnName("command_id")
            .HasMaxLength(OperationsAuditRecord.MaxCommandIdLength).IsRequired();
        builder.Property(record => record.ActorId).HasColumnName("actor_id")
            .HasMaxLength(OperationsAuditRecord.MaxActorIdLength).IsRequired();
        builder.Property(record => record.Action).HasColumnName("action")
            .HasMaxLength(OperationsAuditRecord.MaxActionLength).IsRequired();
        builder.Property(record => record.TransferId).HasColumnName("transfer_id").IsRequired();
        builder.Property(record => record.PreviousState).HasColumnName("previous_state")
            .HasMaxLength(OperationsAuditRecord.MaxStateLength).IsRequired();
        builder.Property(record => record.NewState).HasColumnName("new_state")
            .HasMaxLength(OperationsAuditRecord.MaxStateLength).IsRequired();
        builder.Property(record => record.Reason).HasColumnName("reason")
            .HasMaxLength(OperationsAuditRecord.MaxReasonLength).IsRequired();
        builder.Property(record => record.CorrelationId).HasColumnName("correlation_id").IsRequired();
        builder.Property(record => record.CausationId).HasColumnName("causation_id");
        builder.Property(record => record.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();

        builder.HasIndex(record => record.CommandId).IsUnique();
        builder.HasIndex(record => record.TransferId);
        builder.HasIndex(record => record.CorrelationId);
    }
}
