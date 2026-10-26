using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MiniBanking.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c>. Generating migrations does not open a connection, so a placeholder
/// connection string is enough and no real credentials are needed at design time.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BankingDbContext>
{
    public BankingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BankingDbContext>();
        DependencyInjection.ConfigureDbContext(options, "Host=localhost;Database=mini_banking_design");
        return new BankingDbContext(options.Options);
    }
}
