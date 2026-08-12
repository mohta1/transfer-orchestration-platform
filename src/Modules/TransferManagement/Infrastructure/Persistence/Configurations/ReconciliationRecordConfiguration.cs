using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransferOrchestration.TransferManagement.Application.Reconciliation;
using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.TransferManagement.Infrastructure.Persistence.Configurations;

internal sealed class ReconciliationRecordConfiguration : IEntityTypeConfiguration<ReconciliationRecord>
{
    public void Configure(EntityTypeBuilder<ReconciliationRecord> builder)
    {
        builder.ToTable("reconciliation_records", table =>
        {
            table.HasCheckConstraint("ck_reconciliation_records_attempt_count", "attempt_count >= 0");
            table.HasCheckConstraint(
                "ck_reconciliation_records_status",
                "(status = 1 AND next_attempt_at_utc IS NOT NULL) OR " +
                "(status = 2 AND next_attempt_at_utc IS NULL) OR " +
                "(status = 3 AND next_attempt_at_utc IS NULL)");
            table.HasCheckConstraint("ck_reconciliation_records_timestamps", "updated_at_utc >= created_at_utc");
        });

        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(record => record.TransferId)
            .HasConversion(id => id.Value, value => new TransferId(value))
            .HasColumnName("transfer_id");
        builder.Property(record => record.NetworkSubmissionReference)
            .HasColumnName("network_submission_reference")
            .HasMaxLength(80)
            .IsRequired();
        builder.Property(record => record.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(record => record.AttemptCount).HasColumnName("attempt_count");
        builder.Property(record => record.NextAttemptAtUtc).HasColumnName("next_attempt_at_utc");
        builder.Property(record => record.LastAttemptAtUtc).HasColumnName("last_attempt_at_utc");
        builder.Property(record => record.LastEnquiryResult)
            .HasColumnName("last_enquiry_result")
            .HasMaxLength(32);
        builder.Property(record => record.LastError).HasColumnName("last_error").HasMaxLength(512);
        builder.Property(record => record.LockedBy).HasColumnName("locked_by").HasMaxLength(128);
        builder.Property(record => record.LockedUntilUtc).HasColumnName("locked_until_utc");
        builder.Property(record => record.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(record => record.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(record => record.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasIndex(record => record.TransferId)
            .IsUnique()
            .HasDatabaseName("ux_reconciliation_records_transfer_id");

        builder.HasIndex(record => new { record.NextAttemptAtUtc, record.Id })
            .HasDatabaseName("ix_reconciliation_records_due_work")
            .HasFilter("status = 1 AND next_attempt_at_utc IS NOT NULL");

        builder.HasOne<Transfer>()
            .WithOne()
            .HasForeignKey<ReconciliationRecord>(record => record.TransferId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
