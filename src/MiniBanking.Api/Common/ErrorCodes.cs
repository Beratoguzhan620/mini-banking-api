namespace MiniBanking.Api.Common;

/// <summary>Maps domain error codes to HTTP status codes. Unknown codes are business rule failures (422).</summary>
internal static class ErrorCodes
{
    private static readonly Dictionary<string, int> Statuses = new(StringComparer.Ordinal)
    {
        ["validation_failed"] = StatusCodes.Status400BadRequest,
        ["not_found"] = StatusCodes.Status404NotFound,
        ["conflict"] = StatusCodes.Status409Conflict,
    };

    public static int StatusFor(string code) =>
        Statuses.TryGetValue(code, out var status) ? status : StatusCodes.Status422UnprocessableEntity;
}
