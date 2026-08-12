using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TransferOrchestration.Notification.Infrastructure.Persistence;

internal sealed class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> builder)
    {
        builder.ToTable("processed_messages", table => table.HasCheckConstraint("ck_processed_messages_state",
            "(processed_at_utc IS NOT NULL AND owner_id IS NULL AND claimed_until_utc IS NULL) OR " +
            "(processed_at_utc IS NULL AND owner_id IS NOT NULL AND claimed_until_utc IS NOT NULL)"));
        builder.HasKey(message => new { message.MessageId, message.ConsumerName });
        builder.Property(message => message.MessageId).HasColumnName("message_id");
        builder.Property(message => message.ConsumerName).HasColumnName("consumer_name").HasMaxLength(200);
        builder.Property(message => message.ProcessedAtUtc).HasColumnName("processed_at_utc");
        builder.Property(message => message.OwnerId).HasColumnName("owner_id");
        builder.Property(message => message.ClaimedUntilUtc).HasColumnName("claimed_until_utc");
    }
}
