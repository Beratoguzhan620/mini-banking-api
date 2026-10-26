using System.Security.Cryptography;

namespace MiniBanking.Domain.Accounts;

/// <summary>
/// Account numbers look like <c>MB</c> followed by 16 digits; the last digit is a Luhn check digit so
/// that a mistyped destination number is rejected before it reaches the database.
/// </summary>
public static class AccountNumbers
{
    public const string Prefix = "MB";
    private const int DigitCount = 16;

    public static string Generate()
    {
        Span<char> digits = stackalloc char[DigitCount];
        for (var i = 0; i < DigitCount - 1; i++)
        {
            digits[i] = (char)('0' + RandomNumberGenerator.GetInt32(10));
        }

        digits[DigitCount - 1] = (char)('0' + CheckDigit(digits[..^1]));
        return Prefix + new string(digits);
    }

    public static bool IsValid(string? number)
    {
        if (number is null || number.Length != Prefix.Length + DigitCount || !number.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var digits = number.AsSpan(Prefix.Length);
        foreach (var c in digits)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return digits[^1] - '0' == CheckDigit(digits[..^1]);
    }

    public static string Normalize(string? number) =>
        (number ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();

    private static int CheckDigit(ReadOnlySpan<char> payload)
    {
        var sum = 0;
        var doubleIt = true;
        for (var i = payload.Length - 1; i >= 0; i--)
        {
            var d = payload[i] - '0';
            if (doubleIt)
            {
                d *= 2;
                if (d > 9)
                {
                    d -= 9;
                }
            }

            sum += d;
            doubleIt = !doubleIt;
        }

        return (10 - (sum % 10)) % 10;
    }
}
