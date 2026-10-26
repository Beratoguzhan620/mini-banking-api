using Microsoft.EntityFrameworkCore;
using MiniBanking.Domain.Accounts;
using MiniBanking.Domain.Customers;

namespace MiniBanking.Infrastructure.Persistence;

public sealed class BankingDbContext(DbContextOptions<BankingDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Account> Accounts => Set<Account>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BankingDbContext).Assembly);
    }
}
