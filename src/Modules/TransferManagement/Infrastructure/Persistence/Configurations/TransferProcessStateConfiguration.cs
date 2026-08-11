using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.TransferManagement.Infrastructure.Persistence.Configurations;

internal sealed class TransferProcessStateConfiguration : IEntityTypeConfiguration<TransferProcessState>
{
    public void Configure(EntityTypeBuilder<TransferProcessState> builder)
    {
        builder.ToTable("transfer_process_states", table =>
        {
            table.HasCheckConstraint("ck_transfer_process_states_attempt_count", "attempt_count >= 0");
            table.HasCheckConstraint(
                "ck_transfer_process_states_actionability",
                "(status = 'Active' AND next_action <> 'None' AND next_attempt_at_utc IS NOT NULL AND current_step IN ('Created', 'ActionScheduled')) OR " +
                "(status = 'Waiting' AND next_action = 'None' AND next_attempt_at_utc IS NULL AND current_step = 'WaitingForOutcome') OR " +
                "(status = 'Completed' AND next_action = 'None' AND next_attempt_at_utc IS NULL AND current_step = 'Completed')");
            table.HasCheckConstraint("ck_transfer_process_states_correlation_id", "correlation_id <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("ck_transfer_process_states_timestamps", "updated_at_utc >= created_at_utc");
        });

        builder.HasKey(state => state.TransferId);
        builder.Property(state => state.TransferId)
            .HasConversion(id => id.Value, value => new TransferId(value))
            .HasColumnName("transfer_id")
            .ValueGeneratedNever();
        builder.Property(state => state.CorrelationId).HasColumnName("correlation_id");
        builder.Property(state => state.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
        builder.Property(state => state.CurrentStep).HasColumnName("current_step").HasConversion<string>().HasMaxLength(32);
        builder.Property(state => state.NextAction).HasColumnName("next_action").HasConversion<string>().HasMaxLength(40);
        builder.Property(state => state.AttemptCount).HasColumnName("attempt_count");
        builder.Property(state => state.NetworkSubmissionReference)
            .HasColumnName("network_submission_reference")
            .HasMaxLength(80);
        builder.Property(state => state.NextAttemptAtUtc).HasColumnName("next_attempt_at_utc");
        builder.Property(state => state.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(state => state.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(state => state.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasOne<Transfer>()
            .WithOne()
            .HasForeignKey<TransferProcessState>(state => state.TransferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(state => new { state.NextAttemptAtUtc, state.TransferId })
            .HasDatabaseName("ix_transfer_process_states_due_work")
            .HasFilter("status = 'Active' AND next_action <> 'None' AND next_attempt_at_utc IS NOT NULL");

        builder.HasIndex(state => state.NetworkSubmissionReference)
            .IsUnique()
            .HasDatabaseName("ux_transfer_process_states_network_submission_reference")
            .HasFilter("network_submission_reference IS NOT NULL");
    }
}
