using System.Net;
using MiniBanking.IntegrationTests.Support;
using Npgsql;

namespace MiniBanking.IntegrationTests;

[Collection(ApiCollection.Name)]
public class OperationsTests(ApiFactory factory)
{
    [Fact]
    public async Task Reconciliation_is_healthy_after_normal_traffic()
    {
        var client = await factory.SignedInClientAsync();
        var from = await client.FundedAccountAsync(300m, "EUR");
        var to = await client.OpenAccountAsync("EUR");
        (await client.TransferAsync(from.Id, to.Number, 120m)).EnsureSuccessStatusCode();
        (await client.WithdrawAsync(to.Id, 20m)).EnsureSuccessStatusCode();

        var report = await (await factory.CreateOpsClient().GetAsync("/api/ops/reconciliation")).ReadAsync<ReconciliationDto>();

        Assert.True(report.Healthy);
        Assert.Equal(0, report.UnbalancedTransactions);
        Assert.Empty(report.DriftedAccounts);
        Assert.All(report.Currencies, c => Assert.True(c.Balanced));
        Assert.Contains(report.Currencies, c => c.Currency == "EUR" && c.CustomerBalances >= 280m);
    }

    [Fact]
    public async Task Reconciliation_finds_a_balance_that_was_changed_behind_the_ledgers_back()
    {
        var client = await factory.SignedInClientAsync();
        var account = await client.FundedAccountAsync(10m);

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, $"UPDATE accounts SET balance_minor = balance_minor + 500 WHERE id = '{account.Id}'");
        try
        {
            var report = await (await factory.CreateOpsClient().GetAsync("/api/ops/reconciliation")).ReadAsync<ReconciliationDto>();

            Assert.False(report.Healthy);
            var drifted = Assert.Single(report.DriftedAccounts);
            Assert.Equal(account.Id, drifted.AccountId);
            Assert.Equal(15m, drifted.CachedBalance);
            Assert.Equal(10m, drifted.LedgerBalance);
            Assert.Contains(report.Currencies, c => c.Currency == "TRY" && !c.Balanced);
        }
        finally
        {
            await ExecuteAsync(connection, $"UPDATE accounts SET balance_minor = balance_minor - 500 WHERE id = '{account.Id}'");
        }
    }

    [Fact]
    public async Task Operations_endpoints_need_the_operations_key()
    {
        var anonymous = await factory.CreateClient().GetAsync("/api/ops/reconciliation");
        var customer = await (await factory.SignedInClientAsync()).GetAsync("/api/ops/audit");
        var wrongKey = factory.CreateClient();
        wrongKey.DefaultRequestHeaders.Add("X-Operations-Key", "guess");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, customer.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrongKey.GetAsync("/api/ops/audit")).StatusCode);
    }

    [Fact]
    public async Task Operations_endpoints_do_not_exist_when_no_key_is_configured()
    {
        using var withoutKey = factory.WithWebHostBuilder(builder => builder.UseSetting("Operations:ApiKey", ""));
        var client = withoutKey.CreateClient();
        client.DefaultRequestHeaders.Add("X-Operations-Key", "");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/ops/reconciliation")).StatusCode);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
