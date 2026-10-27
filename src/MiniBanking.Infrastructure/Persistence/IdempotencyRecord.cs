namespace MiniBanking.Infrastructure.Persistence;

/// <summary>
/// A processed request, stored so that a retry with the same key gets the same answer instead of
/// moving money again. The row is inserted at the start of the request and filled in before commit,
/// so a committed row always carries the response.
/// </summary>
public sealed class IdempotencyRecord
{
    public const int KeyMaxLength = 100;

    public Guid CustomerId { get; set; }

    public string Key { get; set; } = string.Empty;

    /// <summary>SHA-256 of method, path and body; a retry must send the same request.</summary>
    public string RequestHash { get; set; } = string.Empty;

    public int? ResponseStatus { get; set; }

    public string? ResponseContentType { get; set; }

    public string? ResponseLocation { get; set; }

    public string? ResponseBody { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
