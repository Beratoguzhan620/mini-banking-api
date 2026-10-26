using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniBanking.Domain.Accounts;
using MiniBanking.Domain.Customers;
using MiniBanking.Domain.Transactions;

namespace MiniBanking.Infrastructure.Persistence.Configurations;

internal sealed class BankTransactionConfiguration : IEntityTypeConfiguration<BankTransaction>
{
    public void Configure(EntityTypeBuilder<BankTransaction> builder)
    {
        builder.ToTable("bank_transactions", table =>
            table.HasCheckConstraint("ck_bank_transactions_amount_positive", "amount_minor > 0"));

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(t => t.Description).HasMaxLength(BankTransaction.DescriptionMaxLength);
        builder.Property(t => t.FailureCode).HasMaxLength(64);
        builder.Property(t => t.FailureReason).HasMaxLength(300);

        builder.HasOne<Account>().WithMany().HasForeignKey(t => t.SourceAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(t => t.DestinationAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany().HasForeignKey(t => t.InitiatedBy).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.InitiatedBy);
    }
}
