using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransferOrchestration.TransferManagement.Infrastructure.Outbox;

namespace TransferOrchestration.TransferManagement.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages", table =>
        {
            table.HasCheckConstraint("ck_outbox_attempts", "\"Attempts\" >= 0");
            table.HasCheckConstraint("ck_outbox_lock", "(\"LockedBy\" IS NULL) = (\"LockedUntilUtc\" IS NULL)");
            table.HasCheckConstraint("ck_outbox_published", "\"Status\" <> 1 OR \"PublishedAtUtc\" IS NOT NULL");
            table.HasCheckConstraint("ck_outbox_dead_letter", "\"Status\" <> 2 OR (\"LockedBy\" IS NULL AND \"LockedUntilUtc\" IS NULL)");
            table.HasCheckConstraint("ck_outbox_failure_pair", "(\"FirstFailureAtUtc\" IS NULL) = (\"LastFailureAtUtc\" IS NULL)");
            table.HasCheckConstraint("ck_outbox_failure_order", "\"FirstFailureAtUtc\" IS NULL OR \"FirstFailureAtUtc\" <= \"LastFailureAtUtc\"");
            table.HasCheckConstraint("ck_outbox_dead_letter_failure", "\"Status\" <> 2 OR \"FirstFailureAtUtc\" IS NOT NULL");
        });
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).UseIdentityByDefaultColumn();
        builder.HasIndex(message => message.MessageId).IsUnique();
        builder.HasIndex(message => new { message.Status, message.NextAttemptAtUtc, message.LockedUntilUtc, message.Id })
            .HasDatabaseName("ix_outbox_eligibility");
        builder.Property(message => message.Type).HasMaxLength(100).IsRequired();
        builder.Property(message => message.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(message => message.LockedBy).HasMaxLength(100);
        builder.Property(message => message.LastError).HasMaxLength(1000);
        builder.Property(message => message.Status).HasConversion<int>();
    }
}
