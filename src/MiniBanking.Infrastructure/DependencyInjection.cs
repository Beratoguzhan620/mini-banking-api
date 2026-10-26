using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using MiniBanking.Infrastructure.Persistence;

namespace MiniBanking.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBankingPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Interceptors registered in DI are picked up, so cross-cutting concerns (and fault injection in
        // tests) can be added without touching this method.
        services.AddDbContext<BankingDbContext>((provider, options) => ConfigureDbContext(options, connectionString)
            .AddInterceptors(provider.GetServices<IInterceptor>()));

        return services;
    }

    internal static DbContextOptionsBuilder ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
