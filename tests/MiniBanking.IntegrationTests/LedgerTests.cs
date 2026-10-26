using System.Net;
using System.Text.Json;
using MiniBanking.IntegrationTests.Support;

namespace MiniBanking.IntegrationTests;

[Collection(ApiCollection.Name)]
public class LedgerTests(ApiFactory factory)
{
    [Fact]
    public async Task Deposit_and_withdrawal_update_the_balance_and_the_history()
    {
        var client = await factory.SignedInClientAsync();
        var account = await client.OpenAccountAsync();

        var deposit = await client.DepositAsync(account.Id, 1_500.75m, "Maaş");
        Assert.Equal(HttpStatusCode.Created, deposit.StatusCode);
        var depositTx = await deposit.ReadAsync<TransactionDto>();
        Assert.Equal("Deposit", depositTx.Type);
        Assert.Equal("Completed", depositTx.Status);
        Assert.Equal(account.Number, depositTx.DestinationAccountNumber);

        (await client.WithdrawAsync(account.Id, 500.25m)).EnsureSuccessStatusCode();

        Assert.Equal(1_000.50m, (await client.GetAccountAsync(account.Id)).Balance);

        var history = await (await client.GetAsync($"/api/accounts/{account.Id}/transactions")).ReadAsync<HistoryPageDto>();
        Assert.Collection(history.Items,
            newest =>
            {
                Assert.Equal("Withdrawal", newest.Type);
                Assert.Equal(-500.25m, newest.Amount);
                Assert.Equal(1_000.50m, newest.BalanceAfter);
            },
            oldest =>
            {
                Assert.Equal("Deposit", oldest.Type);
                Assert.Equal(1_500.75m, oldest.BalanceAfter);
                Assert.Equal("Maaş", oldest.Description);
            });
        Assert.Null(history.NextCursor);

        await factory.AssertHoldAsync();
    }

    [Fact]
    public async Task Withdrawing_more_than_the_balance_fails_and_is_recorded()
    {
        var client = await factory.SignedInClientAsync();
        var account = await client.FundedAccountAsync(100m);

        var response = await client.WithdrawAsync(account.Id, 100.01m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("insufficient_funds", await response.ProblemCodeAsync());

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var transactionId = problem.RootElement.GetProperty("transactionId").GetGuid();
        var failed = await (await client.GetAsync($"/api/transactions/{transactionId}")).ReadAsync<TransactionDto>();
        Assert.Equal("Failed", failed.Status);
        Assert.Equal("insufficient_funds", failed.FailureCode);

        Assert.Equal(100m, (await client.GetAccountAsync(account.Id)).Balance);
        await factory.AssertHoldAsync();
    }

    [Fact]
    public async Task A_frozen_account_takes_deposits_but_refuses_withdrawals()
    {
        var client = await factory.SignedInClientAsync();
        var account = await client.FundedAccountAsync(50m);
        (await client.PostAsync($"/api/accounts/{account.Id}/freeze", null)).EnsureSuccessStatusCode();

        var deposit = await client.DepositAsync(account.Id, 10m);
        var withdrawal = await client.WithdrawAsync(account.Id, 10m);

        Assert.Equal(HttpStatusCode.Created, deposit.StatusCode);
        Assert.Equal("account_frozen", await withdrawal.ProblemCodeAsync());
        Assert.Equal(60m, (await client.GetAccountAsync(account.Id)).Balance);
    }

    [Fact]
    public async Task An_account_with_money_cannot_be_closed()
    {
        var client = await factory.SignedInClientAsync();
        var account = await client.FundedAccountAsync(1m);

        var response = await client.PostAsync($"/api/accounts/{account.Id}/close", null);

        Assert.Equal("account_has_balance", await response.ProblemCodeAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1.234)]
    [InlineData(10_000_000.01)]
    public async Task Invalid_amounts_are_validation_errors(double amount)
    {
        var client = await factory.SignedInClientAsync();
        var account = await client.OpenAccountAsync();

        var response = await client.DepositAsync(account.Id, (decimal)amount);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Nobody_can_move_money_on_someone_elses_account()
    {
        var owner = await factory.SignedInClientAsync();
        var stranger = await factory.SignedInClientAsync();
        var account = await owner.FundedAccountAsync(100m);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DepositAsync(account.Id, 1m)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.WithdrawAsync(account.Id, 1m)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/accounts/{account.Id}/transactions")).StatusCode);
    }

    [Fact]
    public async Task History_pages_newest_first_and_filters_by_type()
    {
        var client = await factory.SignedInClientAsync();
        var account = await client.OpenAccountAsync();
        for (var i = 1; i <= 5; i++)
        {
            (await client.DepositAsync(account.Id, i)).EnsureSuccessStatusCode();
        }

        (await client.WithdrawAsync(account.Id, 1m)).EnsureSuccessStatusCode();

        var amounts = new List<decimal>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var url = $"/api/accounts/{account.Id}/transactions?type=deposit&limit=2" + (cursor is null ? "" : $"&cursor={cursor}");
            var page = await (await client.GetAsync(url)).ReadAsync<HistoryPageDto>();
            amounts.AddRange(page.Items.Select(i => i.Amount));
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null);

        Assert.Equal([5m, 4m, 3m, 2m, 1m], amounts);
        Assert.Equal(3, pages);
    }

    [Theory]
    [InlineData("?limit=0")]
    [InlineData("?limit=101")]
    [InlineData("?type=Refund")]
    [InlineData("?cursor=not-a-cursor")]
    [InlineData("?from=2026-10-02T00:00:00Z&to=2026-10-01T00:00:00Z")]
    public async Task Bad_history_parameters_are_validation_errors(string query)
    {
        var client = await factory.SignedInClientAsync();
        var account = await client.OpenAccountAsync();

        var response = await client.GetAsync($"/api/accounts/{account.Id}/transactions{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
