using System.Text.Json;

namespace MiniBanking.IntegrationTests.Support;

public sealed record AuditEntryDto(long Id, DateTimeOffset OccurredAt, Guid? ActorId, string Action, string TargetType, string? TargetId, string Outcome, string? Details, string? CorrelationId);

public sealed record AuditPageDto(List<AuditEntryDto> Items, string? NextCursor);

public sealed record DriftedAccountDto(Guid AccountId, string AccountNumber, decimal CachedBalance, decimal LedgerBalance);

public sealed record CurrencyPositionDto(string Currency, decimal CustomerBalances, decimal BankPosition, bool Balanced);

public sealed record ReconciliationDto(DateTimeOffset CheckedAt, bool Healthy, int CustomerAccounts, int UnbalancedTransactions, List<DriftedAccountDto> DriftedAccounts, List<CurrencyPositionDto> Currencies);

public static class OpsClient
{
    public static HttpClient CreateOpsClient(this ApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Operations-Key", ApiFactory.OperationsKey);
        return client;
    }

    public static async Task<List<AuditEntryDto>> AuditAsync(this HttpClient ops, string query) =>
        (await (await ops.GetAsync($"/api/ops/audit?{query}")).ReadAsync<AuditPageDto>()).Items;

    public static JsonElement DetailsOf(this AuditEntryDto entry) =>
        JsonDocument.Parse(entry.Details ?? "{}").RootElement.Clone();
}
