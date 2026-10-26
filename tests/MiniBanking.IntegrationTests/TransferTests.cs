using System.Net;
using System.Text.Json;
using MiniBanking.Domain.Accounts;
using MiniBanking.IntegrationTests.Support;

namespace MiniBanking.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TransferTests(ApiFactory factory)
{
    [Fact]
    public async Task A_transfer_moves_money_and_both_sides_see_it()
    {
        var alice = await factory.SignedInClientAsync();
        var bob = await factory.SignedInClientAsync();
        var aliceAccount = await alice.FundedAccountAsync(1_000m);
        var bobAccount = await bob.OpenAccountAsync();

        var response = await alice.TransferAsync(aliceAccount.Id, bobAccount.Number, 250.40m, "Kira payı");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var transfer = await response.ReadAsync<TransactionDto>();
        Assert.Equal("Transfer", transfer.Type);
        Assert.Equal(aliceAccount.Number, transfer.SourceAccountNumber);
        Assert.Equal(bobAccount.Number, transfer.DestinationAccountNumber);

        Assert.Equal(749.60m, (await alice.GetAccountAsync(aliceAccount.Id)).Balance);
        Assert.Equal(250.40m, (await bob.GetAccountAsync(bobAccount.Id)).Balance);

        // The receiver can open the transaction and sees the sender in their history.
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/transactions/{transfer.Id}")).StatusCode);
        var bobHistory = await (await bob.GetAsync($"/api/accounts/{bobAccount.Id}/transactions")).ReadAsync<HistoryPageDto>();
        var received = Assert.Single(bobHistory.Items);
        Assert.Equal(250.40m, received.Amount);
        Assert.Equal(aliceAccount.Number, received.CounterpartyAccountNumber);

        await factory.AssertHoldAsync();
    }

    [Fact]
    public async Task Destination_number_may_be_typed_with_spaces_and_lower_case()
    {
        var client = await factory.SignedInClientAsync();
        var from = await client.FundedAccountAsync(10m);
        var to = await client.OpenAccountAsync();
        var spaced = "mb " + string.Join(' ', to.Number[2..].Chunk(4).Select(c => new string(c)));

        var response = await client.TransferAsync(from.Id, spaced, 1m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Transfers_between_currencies_are_rejected_and_recorded()
    {
        var client = await factory.SignedInClientAsync();
        var lira = await client.FundedAccountAsync(100m, "TRY");
        var euro = await client.OpenAccountAsync("EUR");

        var response = await client.TransferAsync(lira.Id, euro.Number, 10m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("currency_mismatch", await response.ProblemCodeAsync());
        Assert.Equal(100m, (await client.GetAccountAsync(lira.Id)).Balance);
    }

    [Fact]
    public async Task Rules_on_both_ends_are_enforced()
    {
        var client = await factory.SignedInClientAsync();
        var from = await client.FundedAccountAsync(100m);
        var closed = await client.OpenAccountAsync();
        (await client.PostAsync($"/api/accounts/{closed.Id}/close", null)).EnsureSuccessStatusCode();

        Assert.Equal("account_closed", await (await client.TransferAsync(from.Id, closed.Number, 1m)).ProblemCodeAsync());
        Assert.Equal("same_account", await (await client.TransferAsync(from.Id, from.Number, 1m)).ProblemCodeAsync());
        Assert.Equal("insufficient_funds", await (await client.TransferAsync(from.Id, (await client.OpenAccountAsync()).Number, 100.01m)).ProblemCodeAsync());
    }

    [Fact]
    public async Task Unknown_and_malformed_destinations_are_told_apart()
    {
        var client = await factory.SignedInClientAsync();
        var from = await client.FundedAccountAsync(10m);

        var unknown = await client.TransferAsync(from.Id, AccountNumbers.Generate(), 1m);
        var malformed = await client.TransferAsync(from.Id, "MB1234", 1m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);
        Assert.Equal("destination_not_found", await unknown.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
    }

    [Fact]
    public async Task Money_cannot_be_sent_from_someone_elses_account()
    {
        var owner = await factory.SignedInClientAsync();
        var thief = await factory.SignedInClientAsync();
        var victim = await owner.FundedAccountAsync(500m);
        var loot = await thief.OpenAccountAsync();

        var response = await thief.TransferAsync(victim.Id, loot.Number, 500m);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(500m, (await owner.GetAccountAsync(victim.Id)).Balance);
    }

    [Fact]
    public async Task A_failure_in_the_middle_of_a_transfer_rolls_everything_back()
    {
        var client = await factory.SignedInClientAsync();
        var from = await client.FundedAccountAsync(300m);
        var to = await client.OpenAccountAsync();

        // Same database, but this host fails every ledger insert after the balances were updated in memory.
        using var faulty = factory.WithLedgerFailure();
        var faultyClient = faulty.CreateClient();
        faultyClient.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;

        var response = await faultyClient.TransferAsync(from.Id, to.Number, 100m);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("processing_error", await response.ProblemCodeAsync());
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var failed = await (await client.GetAsync($"/api/transactions/{problem.RootElement.GetProperty("transactionId").GetGuid()}")).ReadAsync<TransactionDto>();
        Assert.Equal("Failed", failed.Status);

        Assert.Equal(300m, (await client.GetAccountAsync(from.Id)).Balance);
        Assert.Equal(0m, (await client.GetAccountAsync(to.Id)).Balance);
        await factory.AssertHoldAsync();
    }
}
