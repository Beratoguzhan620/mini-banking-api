namespace MiniBanking.Domain;

/// <summary>
/// Currencies the bank holds accounts in. All of them use two minor units, which keeps the money
/// arithmetic in one place; adding a zero-decimal currency would mean storing the exponent per currency.
/// </summary>
public static class Currencies
{
    public const int MinorUnitsPerMajor = 100;

    private static readonly HashSet<string> SupportedCodes = new(StringComparer.Ordinal) { "TRY", "USD", "EUR" };

    public static IReadOnlyCollection<string> Supported => SupportedCodes;

    public static string Normalize(string? code)
    {
        var normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!SupportedCodes.Contains(normalized))
        {
            throw new DomainException(ErrorCodes.UnsupportedCurrency,
                $"Currency '{code}' is not supported. Use one of: {string.Join(", ", SupportedCodes)}.");
        }

        return normalized;
    }
}
