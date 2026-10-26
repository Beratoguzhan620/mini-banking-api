using System.Net;
using System.Net.Http.Json;
using MiniBanking.IntegrationTests.Support;

namespace MiniBanking.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AccountTests(ApiFactory factory)
{
    [Fact]
    public async Task Opening_an_account_starts_with_a_zero_balance_in_its_currency()
    {
        var client = await factory.SignedInClientAsync();

        var response = await client.PostAsJsonAsync("/api/accounts", new { currency = "eur" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var account = await response.ReadAsync<AccountDto>();
        Assert.Equal("EUR", account.Currency);
        Assert.Equal(0m, account.Balance);
        Assert.Equal("Active", account.Status);
        Assert.Matches("^MB[0-9]{16}$", account.Number);
        Assert.Equal($"/api/accounts/{account.Id}", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task A_customer_can_hold_accounts_in_several_currencies()
    {
        var client = await factory.SignedInClientAsync();
        await client.OpenAccountAsync("TRY");
        await client.OpenAccountAsync("USD");

        var accounts = await (await client.GetAsync("/api/accounts")).ReadAsync<List<AccountDto>>();

        Assert.Equal(["TRY", "USD"], accounts.Select(a => a.Currency));
    }

    [Fact]
    public async Task Unsupported_currency_is_rejected()
    {
        var client = await factory.SignedInClientAsync();

        var response = await client.PostAsJsonAsync("/api/accounts", new { currency = "BTC" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("unsupported_currency", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Another_customers_account_looks_like_it_does_not_exist()
    {
        var owner = await factory.SignedInClientAsync();
        var stranger = await factory.SignedInClientAsync();
        var account = await owner.OpenAccountAsync();

        var get = await stranger.GetAsync($"/api/accounts/{account.Id}");
        var freeze = await stranger.PostAsync($"/api/accounts/{account.Id}/freeze", null);
        var list = await (await stranger.GetAsync("/api/accounts")).ReadAsync<List<AccountDto>>();

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, freeze.StatusCode);
        Assert.Empty(list);
    }

    [Fact]
    public async Task Freeze_unfreeze_and_close_follow_the_account_rules()
    {
        var client = await factory.SignedInClientAsync();
        var account = await client.OpenAccountAsync();
        var path = $"/api/accounts/{account.Id}";

        var frozen = await (await client.PostAsync($"{path}/freeze", null)).ReadAsync<AccountDto>();
        Assert.Equal("Frozen", frozen.Status);

        var freezeAgain = await client.PostAsync($"{path}/freeze", null);
        Assert.Equal("account_frozen", await freezeAgain.ProblemCodeAsync());

        var active = await (await client.PostAsync($"{path}/unfreeze", null)).ReadAsync<AccountDto>();
        Assert.Equal("Active", active.Status);

        var closed = await (await client.PostAsync($"{path}/close", null)).ReadAsync<AccountDto>();
        Assert.Equal("Closed", closed.Status);
        Assert.NotNull(closed.ClosedAt);

        var reopen = await client.PostAsync($"{path}/unfreeze", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reopen.StatusCode);
        Assert.Equal("account_closed", await reopen.ProblemCodeAsync());
    }
}
