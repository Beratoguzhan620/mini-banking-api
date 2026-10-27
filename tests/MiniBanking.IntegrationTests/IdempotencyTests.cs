using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MiniBanking.IntegrationTests.Support;

namespace MiniBanking.IntegrationTests;

[Collection(ApiCollection.Name)]
public class IdempotencyTests(ApiFactory factory)
{
    [Fact]
    public async Task Money_requests_without_a_key_are_rejected_before_anything_moves()
    {
        var client = await factory.SignedInClientAsync();
        var account = await client.OpenAccountAsync();

        var response = await client.PostAsJsonAsync($"/api/accounts/{account.Id}/deposits", new { amount = 10m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("idempotency_key_missing", await response.ProblemCodeAsync());
        Assert.Equal(0m, (await client.GetAccountAsync(account.Id)).Balance);
    }

    [Fact]
    public async Task A_key_longer_than_the_limit_is_rejected()
    {
        var client = await factory.SignedInClientAsync();
        var account = await client.OpenAccountAsync();

        var response = await client.DepositAsync(account.Id, 10m, idempotencyKey: new string('k', 101));

        Assert.Equal("idempotency_key_invalid", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Retrying_with_the_same_key_replays_the_first_response()
    {
        var client = await factory.SignedInClientAsync();
        var from = await client.FundedAccountAsync(100m);
        var to = await client.OpenAccountAsync();
        var key = Guid.NewGuid().ToString();

        var first = await client.TransferAsync(from.Id, to.Number, 40m, idempotencyKey: key);
        var retry = await client.TransferAsync(from.Id, to.Number, 40m, idempotencyKey: key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.False(first.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal("true", Assert.Single(retry.Headers.GetValues("Idempotent-Replayed")));
        Assert.Equal(first.Headers.Location, retry.Headers.Location);
        Assert.Equal((await first.ReadAsync<TransactionDto>()).Id, (await retry.ReadAsync<TransactionDto>()).Id);

        Assert.Equal(60m, (await client.GetAccountAsync(from.Id)).Balance);
        Assert.Equal(40m, (await client.GetAccountAsync(to.Id)).Balance);
        await factory.AssertHoldAsync();
    }

    [Fact]
    public async Task Reusing_a_key_for_a_different_request_is_refused()
    {
        var client = await factory.SignedInClientAsync();
        var account = await client.OpenAccountAsync();
        var key = Guid.NewGuid().ToString();

        (await client.DepositAsync(account.Id, 10m, idempotencyKey: key)).EnsureSuccessStatusCode();
        var different = await client.DepositAsync(account.Id, 99m, idempotencyKey: key);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, different.StatusCode);
        Assert.Equal("idempotency_key_reused", await different.ProblemCodeAsync());
        Assert.Equal(10m, (await client.GetAccountAsync(account.Id)).Balance);
    }

    [Fact]
    public async Task Keys_belong_to_one_customer()
    {
        var alice = await factory.SignedInClientAsync();
        var bob = await factory.SignedInClientAsync();
        var aliceAccount = await alice.OpenAccountAsync();
        var bobAccount = await bob.OpenAccountAsync();
        const string sharedKey = "payment-1";

        var fromAlice = await alice.DepositAsync(aliceAccount.Id, 5m, idempotencyKey: sharedKey);
        var fromBob = await bob.DepositAsync(bobAccount.Id, 5m, idempotencyKey: sharedKey);

        Assert.Equal(HttpStatusCode.Created, fromAlice.StatusCode);
        Assert.Equal(HttpStatusCode.Created, fromBob.StatusCode);
        Assert.False(fromBob.Headers.Contains("Idempotent-Replayed"));
    }

    [Fact]
    public async Task A_business_failure_is_replayed_too_even_after_the_balance_changed()
    {
        var client = await factory.SignedInClientAsync();
        var account = await client.FundedAccountAsync(10m);
        var key = Guid.NewGuid().ToString();

        var first = await client.WithdrawAsync(account.Id, 50m, idempotencyKey: key);
        (await client.DepositAsync(account.Id, 100m)).EnsureSuccessStatusCode();
        var retry = await client.WithdrawAsync(account.Id, 50m, idempotencyKey: key);

        Assert.Equal("insufficient_funds", await first.ProblemCodeAsync());
        Assert.Equal("insufficient_funds", await retry.ProblemCodeAsync());
        Assert.Equal(await TransactionIdOf(first), await TransactionIdOf(retry));
        Assert.Equal(110m, (await client.GetAccountAsync(account.Id)).Balance);
    }

    [Fact]
    public async Task A_rejected_request_does_not_burn_the_key()
    {
        var client = await factory.SignedInClientAsync();
        var account = await client.OpenAccountAsync();
        var key = Guid.NewGuid().ToString();

        var invalid = await client.DepositAsync(account.Id, -1m, idempotencyKey: key);
        var corrected = await client.DepositAsync(account.Id, 1m, idempotencyKey: key);

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.Created, corrected.StatusCode);
    }

    [Fact]
    public async Task Identical_requests_sent_at_the_same_moment_move_money_once()
    {
        var client = await factory.SignedInClientAsync();
        var from = await client.FundedAccountAsync(100m);
        var to = await client.OpenAccountAsync();
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => client.TransferAsync(from.Id, to.Number, 25m, idempotencyKey: key)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Single(responses, r => !r.Headers.Contains("Idempotent-Replayed"));
        var ids = await Task.WhenAll(responses.Select(async r => (await r.ReadAsync<TransactionDto>()).Id));
        Assert.Single(ids.Distinct());

        Assert.Equal(75m, (await client.GetAccountAsync(from.Id)).Balance);
        Assert.Equal(25m, (await client.GetAccountAsync(to.Id)).Balance);
        await factory.AssertHoldAsync();
    }

    private static async Task<Guid> TransactionIdOf(HttpResponseMessage problem)
    {
        using var doc = JsonDocument.Parse(await problem.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("transactionId").GetGuid();
    }
}
