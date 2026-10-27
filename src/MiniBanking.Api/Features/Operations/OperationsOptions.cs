namespace MiniBanking.Api.Features.Operations;

internal sealed class OperationsOptions
{
    public const string Section = "Operations";

    /// <summary>
    /// Key for the back-office endpoints, sent as <c>X-Operations-Key</c>. When it is not configured the
    /// endpoints answer 404, so a deployment without it exposes nothing.
    /// </summary>
    public string? ApiKey { get; set; }
}
