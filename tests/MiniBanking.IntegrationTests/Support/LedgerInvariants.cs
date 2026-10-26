using Npgsql;

namespace MiniBanking.IntegrationTests.Support;

/// <summary>
/// Checks the two rules that must hold no matter what the tests did: every transaction's entries sum
/// to zero, and every customer account's cached balance equals the sum of its ledger entries.
/// </summary>
public static class LedgerInvariants
{
    public static async Task AssertHoldAsync(this ApiFactory factory)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();

        var unbalanced = await CountAsync(connection, """
            SELECT count(*) FROM (
                SELECT transaction_id FROM ledger_entries GROUP BY transaction_id HAVING sum(amount_minor) <> 0
            ) t
            """);
        Assert.Equal(0, unbalanced);

        var drifted = await CountAsync(connection, """
            SELECT count(*) FROM accounts a
            WHERE a.kind = 'Customer'
              AND a.balance_minor <> COALESCE((SELECT sum(e.amount_minor) FROM ledger_entries e WHERE e.account_id = a.id), 0)
            """);
        Assert.Equal(0, drifted);

        var negative = await CountAsync(connection, "SELECT count(*) FROM accounts WHERE kind = 'Customer' AND balance_minor < 0");
        Assert.Equal(0, negative);
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
