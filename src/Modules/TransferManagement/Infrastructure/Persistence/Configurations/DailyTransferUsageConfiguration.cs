using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TransferOrchestration.TransferManagement.Infrastructure.Persistence.Configurations;

internal sealed class DailyTransferUsageConfiguration : IEntityTypeConfiguration<DailyTransferUsage>
{
    public void Configure(EntityTypeBuilder<DailyTransferUsage> builder)
    {
        builder.ToTable("daily_transfer_usages");
        builder.HasKey(usage => new { usage.SourceAccountId, usage.Currency, usage.UtcDay });
        builder.Property(usage => usage.SourceAccountId).HasColumnName("source_account_id");
        builder.Property(usage => usage.Currency).HasColumnName("currency").HasMaxLength(3);
        builder.Property(usage => usage.UtcDay).HasColumnName("utc_day");
        builder.Property(usage => usage.ConsumedAmount).HasColumnName("consumed_amount").HasPrecision(19, 4);
        builder.ToTable(table => table.HasCheckConstraint("ck_daily_transfer_usage_positive", "consumed_amount > 0"));
    }
}
