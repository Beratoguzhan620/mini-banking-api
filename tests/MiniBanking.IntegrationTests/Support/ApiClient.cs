using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniBanking.IntegrationTests.Support;

public sealed record AccountDto(Guid Id, string Number, string Currency, decimal Balance, string Status, DateTimeOffset OpenedAt, DateTimeOffset? ClosedAt);

public sealed record TokenDto(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

public sealed record CustomerDto(Guid Id, string Email, string FullName, DateTimeOffset CreatedAt);

public sealed record TransactionDto(
    Guid Id, string Type, string Status, string Currency, decimal Amount,
    string? SourceAccountNumber, string? DestinationAccountNumber, string? Description, string? FailureCode, DateTimeOffset CreatedAt);

public sealed record HistoryItemDto(Guid TransactionId, string Type, decimal Amount, decimal BalanceAfter, string? CounterpartyAccountNumber, string? Description, DateTimeOffset CreatedAt);

public sealed record HistoryPageDto(List<HistoryItemDto> Items, string? NextCursor);

/// <summary>Thin helpers over HttpClient so tests read like the API flow they exercise.</summary>
public static class ApiClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public const string Password = "correct-horse-battery";

    public static string UniqueEmail(string prefix = "customer") => $"{prefix}-{Guid.NewGuid():N}@example.com";

    /// <summary>Registers a fresh customer and returns a client that sends their token.</summary>
    public static async Task<HttpClient> SignedInClientAsync(this ApiFactory factory, string? email = null)
    {
        email ??= UniqueEmail();
        var client = factory.CreateClient();

        var register = await client.PostAsJsonAsync("/api/auth/register", new { email, fullName = "Test Müşteri", password = Password });
        register.EnsureSuccessStatusCode();

        var token = await client.LoginAsync(email, Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }

    public static async Task<TokenDto> LoginAsync(this HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenDto>(Json))!;
    }

    public static async Task<AccountDto> OpenAccountAsync(this HttpClient client, string currency = "TRY")
    {
        var response = await client.PostAsJsonAsync("/api/accounts", new { currency });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountDto>(Json))!;
    }

    public const string IdempotencyHeader = "Idempotency-Key";

    /// <summary>Money-moving POST with an idempotency key; a fresh key per call unless one is given.</summary>
    public static Task<HttpResponseMessage> PostMoneyAsync(this HttpClient client, string url, object body, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add(IdempotencyHeader, idempotencyKey ?? Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> DepositAsync(this HttpClient client, Guid accountId, decimal amount, string? description = null, string? idempotencyKey = null) =>
        client.PostMoneyAsync($"/api/accounts/{accountId}/deposits", new { amount, description }, idempotencyKey);

    public static Task<HttpResponseMessage> WithdrawAsync(this HttpClient client, Guid accountId, decimal amount, string? idempotencyKey = null) =>
        client.PostMoneyAsync($"/api/accounts/{accountId}/withdrawals", new { amount }, idempotencyKey);

    public static Task<HttpResponseMessage> TransferAsync(this HttpClient client, Guid sourceAccountId, string destinationAccountNumber, decimal amount, string? description = null, string? idempotencyKey = null) =>
        client.PostMoneyAsync("/api/transfers", new { sourceAccountId, destinationAccountNumber, amount, description }, idempotencyKey);

    public static async Task<AccountDto> GetAccountAsync(this HttpClient client, Guid accountId) =>
        await (await client.GetAsync($"/api/accounts/{accountId}")).ReadAsync<AccountDto>();

    /// <summary>Opens an account and deposits <paramref name="balance"/> into it.</summary>
    public static async Task<AccountDto> FundedAccountAsync(this HttpClient client, decimal balance, string currency = "TRY")
    {
        var account = await client.OpenAccountAsync(currency);
        (await client.DepositAsync(account.Id, balance)).EnsureSuccessStatusCode();
        return await client.GetAccountAsync(account.Id);
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    /// <summary>The stable <c>code</c> field of a problem response.</summary>
    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
