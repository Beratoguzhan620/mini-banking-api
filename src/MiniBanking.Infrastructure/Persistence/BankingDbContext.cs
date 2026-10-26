using Microsoft.EntityFrameworkCore;
using MiniBanking.Domain.Accounts;
using MiniBanking.Domain.Customers;
using MiniBanking.Domain.Transactions;

namespace MiniBanking.Infrastructure.Persistence;

public sealed class BankingDbContext(DbContextOptions<BankingDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<BankTransaction> BankTransactions => Set<BankTransaction>();

    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BankingDbContext).Assembly);
    }
}
