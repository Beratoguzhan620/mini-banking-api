namespace MiniBanking.Api.Logging;

/// <summary>Shortens personal data to what is still useful when reading logs or the audit trail.</summary>
internal static class Masking
{
    public const string Hidden = "***";

    /// <summary><c>ayse.yilmaz@example.com</c> → <c>a***@example.com</c>.</summary>
    public static string Email(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Hidden;
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at <= 0 ? Hidden : $"{email[0]}{Hidden}{email[at..]}";
    }

    /// <summary><c>MB1234567890123456</c> → <c>MB************3456</c>.</summary>
    public static string AccountNumber(string? number)
    {
        if (string.IsNullOrEmpty(number) || number.Length <= 6)
        {
            return Hidden;
        }

        return number[..2] + new string('*', number.Length - 6) + number[^4..];
    }
}
