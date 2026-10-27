namespace MiniBanking.Api.Features.Idempotency;

/// <summary>Endpoint metadata: the request must carry an <c>Idempotency-Key</c> header.</summary>
internal sealed class RequiresIdempotencyKey
{
    public static readonly RequiresIdempotencyKey Instance = new();
}

internal static class IdempotencyEndpointExtensions
{
    public static RouteHandlerBuilder RequireIdempotencyKey(this RouteHandlerBuilder builder) =>
        builder.WithMetadata(RequiresIdempotencyKey.Instance);
}
