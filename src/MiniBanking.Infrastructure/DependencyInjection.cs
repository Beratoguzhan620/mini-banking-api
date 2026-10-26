using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MiniBanking.Infrastructure.Persistence;

namespace MiniBanking.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBankingPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<BankingDbContext>(options => ConfigureDbContext(options, connectionString));

        return services;
    }

    internal static DbContextOptionsBuilder ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
