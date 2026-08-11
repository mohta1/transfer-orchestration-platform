using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransferOrchestration.TransferManagement.Domain.Transfers;

namespace TransferOrchestration.TransferManagement.Infrastructure.Persistence.Configurations;

internal sealed class TransferConfiguration : IEntityTypeConfiguration<Transfer>
{
    public void Configure(EntityTypeBuilder<Transfer> builder)
    {
        builder.ToTable("transfers");

        builder.HasKey(transfer => transfer.Id);
        builder.Property(transfer => transfer.Id)
            .HasConversion(id => id.Value, value => new TransferId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(transfer => transfer.SourceAccountId).HasColumnName("source_account_id");
        builder.Property(transfer => transfer.DestinationAccountId).HasColumnName("destination_account_id");
        builder.Property(transfer => transfer.Amount).HasColumnName("amount").HasPrecision(19, 4);
        builder.Property(transfer => transfer.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength();
        builder.Property(transfer => transfer.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(32);
        builder.Property(transfer => transfer.State).HasColumnName("state").HasConversion<string>().HasMaxLength(40);
        builder.Property(transfer => transfer.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(transfer => transfer.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.Ignore(transfer => transfer.DomainEvents);
    }
}
