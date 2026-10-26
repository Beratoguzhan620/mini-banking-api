using System.Net;
using MiniBanking.IntegrationTests.Support;

namespace MiniBanking.IntegrationTests;

/// <summary>
/// The hard part of the project: many requests touching the same accounts at the same moment.
/// These run against real PostgreSQL row locks, not a mock.
/// </summary>
[Collection(ApiCollection.Name)]
public class ConcurrencyTests(ApiFactory factory)
{
    [Fact]
    public async Task Parallel_transfers_from_one_account_never_overdraw_it()
    {
        var client = await factory.SignedInClientAsync();
        var source = await client.FundedAccountAsync(1_000m);
        var destination = await client.OpenAccountAsync();

        // 100 × 15.00 = 1,500.00 requested against 1,000.00 available: exactly 66 can succeed.
        var responses = await Task.WhenAll(Enumerable.Range(0, 100)
            .Select(_ => client.TransferAsync(source.Id, destination.Number, 15m)));

        var succeeded = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var rejected = responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity);
        Assert.Equal(66, succeeded);
        Assert.Equal(34, rejected);
        foreach (var response in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
        {
            Assert.Equal("insufficient_funds", await response.ProblemCodeAsync());
        }

        Assert.Equal(10m, (await client.GetAccountAsync(source.Id)).Balance);
        Assert.Equal(990m, (await client.GetAccountAsync(destination.Id)).Balance);
        await factory.AssertHoldAsync();
    }

    [Fact]
    public async Task Transfers_in_opposite_directions_at_the_same_time_do_not_deadlock()
    {
        var alice = await factory.SignedInClientAsync();
        var bob = await factory.SignedInClientAsync();
        var a = await alice.FundedAccountAsync(1_000m);
        var b = await bob.FundedAccountAsync(1_000m);

        var transfers = Enumerable.Range(0, 100).Select(i => i % 2 == 0
            ? alice.TransferAsync(a.Id, b.Number, 10m)
            : bob.TransferAsync(b.Id, a.Number, 10m));
        var responses = await Task.WhenAll(transfers);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(1_000m, (await alice.GetAccountAsync(a.Id)).Balance);
        Assert.Equal(1_000m, (await bob.GetAccountAsync(b.Id)).Balance);
        await factory.AssertHoldAsync();
    }

    [Fact]
    public async Task Deposits_withdrawals_and_transfers_racing_on_one_account_keep_the_ledger_exact()
    {
        var client = await factory.SignedInClientAsync();
        var hub = await client.FundedAccountAsync(500m);
        var other = await client.FundedAccountAsync(500m);

        var operations = Enumerable.Range(0, 90).Select(i => (i % 3) switch
        {
            0 => client.DepositAsync(hub.Id, 7m),
            1 => client.WithdrawAsync(hub.Id, 5m),
            _ => client.TransferAsync(other.Id, hub.Number, 3m),
        });
        var responses = await Task.WhenAll(operations);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        // 30 × (7 − 5 + 3) = 150 more on the hub, 90 less on the other account.
        Assert.Equal(650m, (await client.GetAccountAsync(hub.Id)).Balance);
        Assert.Equal(410m, (await client.GetAccountAsync(other.Id)).Balance);
        await factory.AssertHoldAsync();
    }
}
