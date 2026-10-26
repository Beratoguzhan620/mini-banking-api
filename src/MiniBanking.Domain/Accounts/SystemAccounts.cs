namespace MiniBanking.Domain.Accounts;

/// <summary>
/// The bank's own side of deposits and withdrawals, one per currency, with fixed ids so they can be
/// seeded by a migration and found without a query.
/// </summary>
public static class SystemAccounts
{
    private static readonly Dictionary<string, Guid> Ids = new(StringComparer.Ordinal)
    {
        ["TRY"] = new("00000000-0000-7000-8000-000000000001"),
        ["USD"] = new("00000000-0000-7000-8000-000000000002"),
        ["EUR"] = new("00000000-0000-7000-8000-000000000003"),
    };

    public static IReadOnlyDictionary<string, Guid> All => Ids;

    public static Guid IdFor(string currency) =>
        Ids.TryGetValue(currency, out var id)
            ? id
            : throw new InvalidOperationException($"No system account is defined for {currency}.");

    public static string NumberFor(string currency) => $"SYS-{currency}";
}
