using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniBanking.Domain.Accounts;
using MiniBanking.Domain.Customers;

namespace MiniBanking.Infrastructure.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable(table =>
        {
            // The application checks these too; the database is the last line of defence.
            table.HasCheckConstraint("ck_accounts_customer_balance_non_negative", "kind = 'System' OR balance_minor >= 0");
            table.HasCheckConstraint("ck_accounts_customer_has_owner", "kind = 'System' OR customer_id IS NOT NULL");
        });

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Number).HasMaxLength(18).IsRequired();
        builder.Property(a => a.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.Version).IsRowVersion();

        builder.HasIndex(a => a.Number).IsUnique();
        builder.HasIndex(a => a.CustomerId);

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(a => a.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
