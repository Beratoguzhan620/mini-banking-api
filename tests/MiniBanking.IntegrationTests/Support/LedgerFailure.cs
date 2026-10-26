using System.Data.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace MiniBanking.IntegrationTests.Support;

/// <summary>Simulates the database failing halfway through a money movement.</summary>
public static class LedgerFailure
{
    public static WebApplicationFactory<Program> WithLedgerFailure(this ApiFactory factory) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IInterceptor, FailLedgerInserts>()));

    private sealed class FailLedgerInserts : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO ledger_entries", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Simulated database failure while writing ledger entries.");
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
