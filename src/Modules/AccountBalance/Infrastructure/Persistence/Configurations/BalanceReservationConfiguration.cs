using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TransferOrchestration.AccountBalance.Domain.Accounts;

namespace TransferOrchestration.AccountBalance.Infrastructure.Persistence.Configurations;

internal sealed class BalanceReservationConfiguration : IEntityTypeConfiguration<BalanceReservation>
{
    public void Configure(EntityTypeBuilder<BalanceReservation> builder)
    {
        builder.ToTable("balance_reservations", table =>
            table.HasCheckConstraint("ck_balance_reservations_amount_positive", "amount > 0"));

        builder.HasKey(reservation => reservation.Id);
        builder.Property(reservation => reservation.Id)
            .HasConversion(id => id.Value, value => new BalanceReservationId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property<AccountId>("account_id")
            .HasConversion(id => id.Value, value => new AccountId(value))
            .HasColumnName("account_id");
        builder.Property(reservation => reservation.TransferId).HasColumnName("transfer_id");
        builder.HasIndex(reservation => reservation.TransferId)
            .IsUnique()
            .HasDatabaseName("ux_balance_reservations_transfer_id");
        builder.Property(reservation => reservation.Amount).HasColumnName("amount").HasPrecision(19, 4);
        builder.Property(reservation => reservation.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
        builder.Property(reservation => reservation.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(reservation => reservation.FinalisedAtUtc).HasColumnName("finalised_at_utc");
    }
}
