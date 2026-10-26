namespace MiniBanking.Domain;

/// <summary>Converts between API amounts (decimal, major units) and stored amounts (long, minor units).</summary>
public static class Amounts
{
    /// <summary>Upper limit for a single operation. Keeps typos like an extra zero row of digits out of the ledger.</summary>
    public const decimal MaxPerOperation = 10_000_000m;

    public static bool TryToMinor(decimal amount, out long minor)
    {
        minor = 0;
        if (amount <= 0 || amount > MaxPerOperation || decimal.Round(amount, 2) != amount)
        {
            return false;
        }

        minor = (long)(amount * Currencies.MinorUnitsPerMajor);
        return true;
    }

    public static decimal ToMajor(long minor) => (decimal)minor / Currencies.MinorUnitsPerMajor;
}
