namespace MiniBanking.Domain;

/// <summary>Stable error codes returned to API clients. Never rename a published value.</summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string NotFound = "not_found";
    public const string EmailTaken = "email_taken";
    public const string InvalidCredentials = "invalid_credentials";
    public const string UnsupportedCurrency = "unsupported_currency";
    public const string AccountClosed = "account_closed";
    public const string AccountFrozen = "account_frozen";
    public const string AccountNotFrozen = "account_not_frozen";
    public const string AccountHasBalance = "account_has_balance";
    public const string ConcurrentUpdate = "concurrent_update";
    public const string InsufficientFunds = "insufficient_funds";
    public const string CurrencyMismatch = "currency_mismatch";
    public const string SameAccount = "same_account";
    public const string ProcessingError = "processing_error";
}
