using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransferOrchestration.AccountBalance.Domain.Accounts;

namespace TransferOrchestration.AccountBalance.Infrastructure.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts", table =>
        {
            table.HasCheckConstraint("ck_accounts_available_balance_non_negative", "available_balance >= 0");
            table.HasCheckConstraint("ck_accounts_reserved_balance_non_negative", "reserved_balance >= 0");
        });

        builder.HasKey(account => account.Id);
        builder.Property(account => account.Id)
            .HasConversion(id => id.Value, value => new AccountId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(account => account.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength();
        builder.Property(account => account.AvailableBalance).HasColumnName("available_balance").HasPrecision(19, 4);
        builder.Property(account => account.ReservedBalance).HasColumnName("reserved_balance").HasPrecision(19, 4);
        builder.Property(account => account.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
        builder.Property(account => account.Version).HasColumnName("version").IsConcurrencyToken();

        builder.HasMany(account => account.Reservations)
            .WithOne()
            .HasForeignKey("account_id")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(account => account.Reservations)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(account => account.DomainEvents);
    }
}
