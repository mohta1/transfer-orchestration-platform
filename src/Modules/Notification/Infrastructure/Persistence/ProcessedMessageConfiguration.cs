using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TransferOrchestration.Notification.Infrastructure.Persistence;

internal sealed class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> builder)
    {
        builder.ToTable("processed_messages");
        builder.HasKey(message => new { message.MessageId, message.ConsumerName });
        builder.Property(message => message.MessageId).HasColumnName("message_id");
        builder.Property(message => message.ConsumerName).HasColumnName("consumer_name").HasMaxLength(200);
        builder.Property(message => message.ProcessedAtUtc).HasColumnName("processed_at_utc");
    }
}
