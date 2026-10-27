using MiniBanking.Domain;

namespace MiniBanking.Api.Common;

/// <summary>Maps domain error codes to HTTP status codes. Unknown codes are business rule failures (422).</summary>
internal static class ErrorStatuses
{
    private static readonly Dictionary<string, int> Statuses = new(StringComparer.Ordinal)
    {
        [ErrorCodes.ValidationFailed] = StatusCodes.Status400BadRequest,
        [ErrorCodes.IdempotencyKeyMissing] = StatusCodes.Status400BadRequest,
        [ErrorCodes.IdempotencyKeyInvalid] = StatusCodes.Status400BadRequest,
        [ErrorCodes.InvalidCredentials] = StatusCodes.Status401Unauthorized,
        [ErrorCodes.NotFound] = StatusCodes.Status404NotFound,
        [ErrorCodes.EmailTaken] = StatusCodes.Status409Conflict,
        [ErrorCodes.ConcurrentUpdate] = StatusCodes.Status409Conflict,
        [ErrorCodes.ProcessingError] = StatusCodes.Status500InternalServerError,
    };

    public static int StatusFor(string code) =>
        Statuses.TryGetValue(code, out var status) ? status : StatusCodes.Status422UnprocessableEntity;
}
