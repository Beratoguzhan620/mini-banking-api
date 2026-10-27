using System.Net;
using System.Net.Http.Json;
using MiniBanking.IntegrationTests.Support;
using Npgsql;

namespace MiniBanking.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuditTests(ApiFactory factory)
{
    [Fact]
    public async Task A_transfer_is_audited_with_its_actor_and_correlation_id()
    {
        var client = await factory.SignedInClientAsync();
        var me = await (await client.GetAsync("/api/me")).ReadAsync<CustomerDto>();
        var from = await client.FundedAccountAsync(50m);
        var to = await client.OpenAccountAsync();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/transfers")
        {
            Content = JsonContent.Create(new { sourceAccountId = from.Id, destinationAccountNumber = to.Number, amount = 20m }),
        };
        request.Headers.Add(ApiClient.IdempotencyHeader, Guid.NewGuid().ToString());
        request.Headers.Add("X-Correlation-Id", "audit-test-1");
        var transfer = await (await client.SendAsync(request)).ReadAsync<TransactionDto>();

        var entry = Assert.Single(await factory.CreateOpsClient().AuditAsync($"targetId={transfer.Id}"));
        Assert.Equal("transfer.completed", entry.Action);
        Assert.Equal("Succeeded", entry.Outcome);
        Assert.Equal(me.Id, entry.ActorId);
        Assert.Equal("audit-test-1", entry.CorrelationId);
        Assert.Equal(2000, entry.DetailsOf().GetProperty("amountMinor").GetInt64());
    }

    [Fact]
    public async Task A_rejected_withdrawal_is_audited_with_its_reason()
    {
        var client = await factory.SignedInClientAsync();
        var account = await client.FundedAccountAsync(1m);

        var response = await client.WithdrawAsync(account.Id, 5m);
        var transactionId = (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("transactionId").GetGuid();

        var entry = Assert.Single(await factory.CreateOpsClient().AuditAsync($"targetId={transactionId}"));
        Assert.Equal("withdrawal.failed", entry.Action);
        Assert.Equal("Rejected", entry.Outcome);
        Assert.Equal("insufficient_funds", entry.DetailsOf().GetProperty("code").GetString());
    }

    [Fact]
    public async Task Account_lifecycle_changes_are_audited()
    {
        var client = await factory.SignedInClientAsync();
        var account = await client.OpenAccountAsync("USD");
        (await client.PostAsync($"/api/accounts/{account.Id}/freeze", null)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/accounts/{account.Id}/unfreeze", null)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/accounts/{account.Id}/close", null)).EnsureSuccessStatusCode();

        var actions = (await factory.CreateOpsClient().AuditAsync($"targetId={account.Id}")).Select(e => e.Action);

        Assert.Equal(["account.closed", "account.unfrozen", "account.frozen", "account.opened"], actions);
    }

    [Fact]
    public async Task A_failed_login_is_audited_without_the_password_or_the_full_email()
    {
        var client = factory.CreateClient();
        var email = ApiClient.UniqueEmail("guess");
        const string password = "not-the-password-42";

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var correlationId = response.Headers.GetValues("X-Correlation-Id").Single();

        var entry = Assert.Single(await factory.CreateOpsClient().AuditAsync($"correlationId={correlationId}"));
        Assert.Equal("auth.login_failed", entry.Action);
        Assert.Equal("Rejected", entry.Outcome);
        Assert.Null(entry.ActorId);
        Assert.Equal("g***@example.com", entry.DetailsOf().GetProperty("email").GetString());
        Assert.DoesNotContain(password, entry.Details);
        Assert.DoesNotContain(email, entry.Details);
    }

    [Fact]
    public async Task The_audit_trail_cannot_be_edited_or_deleted()
    {
        await factory.SignedInClientAsync();
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();

        foreach (var sql in new[] { "UPDATE audit_log SET action = 'tampered'", "DELETE FROM audit_log" })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Contains("append-only", error.MessageText);
        }
    }
}
