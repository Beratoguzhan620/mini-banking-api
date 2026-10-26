using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniBanking.Domain.Accounts;
using MiniBanking.Domain.Transactions;

namespace MiniBanking.Infrastructure.Persistence.Configurations;

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable(table =>
            table.HasCheckConstraint("ck_ledger_entries_amount_not_zero", "amount_minor <> 0"));

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).UseIdentityAlwaysColumn();

        builder.HasOne<BankTransaction>().WithMany().HasForeignKey(e => e.TransactionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(e => e.AccountId).OnDelete(DeleteBehavior.Restrict);

        // History reads one account newest-first and pages by id.
        builder.HasIndex(e => new { e.AccountId, e.Id }).IsDescending(false, true);
        builder.HasIndex(e => e.TransactionId);
    }
}
