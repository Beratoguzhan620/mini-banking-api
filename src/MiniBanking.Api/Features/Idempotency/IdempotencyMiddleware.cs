using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using MiniBanking.Api.Common;
using MiniBanking.Domain;
using MiniBanking.Infrastructure.Persistence;

namespace MiniBanking.Api.Features.Idempotency;

/// <summary>
/// Makes money-moving requests safe to retry.
/// <list type="number">
/// <item>Open a database transaction and insert the key row. An identical request already running holds
/// that row, so PostgreSQL makes this insert wait until the first one commits.</item>
/// <item>If the key exists: same request → replay the stored response; different request → 422.</item>
/// <item>Otherwise run the endpoint inside the same transaction, store its response on the key row and
/// commit. The money and the key are saved together, so a crash can never leave one without the other.</item>
/// </list>
/// It sits outside the exception handler so that error responses are captured as well.
/// </summary>
internal sealed class IdempotencyMiddleware(RequestDelegate next)
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeaderName = "Idempotent-Replayed";

    public async Task InvokeAsync(HttpContext context, BankingDbContext db, TimeProvider clock,
        IProblemDetailsService problems, ILogger<IdempotencyMiddleware> logger)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<RequiresIdempotencyKey>() is null
            || context.User.Identity?.IsAuthenticated != true)
        {
            // Unauthenticated calls fall through so authorization answers 401 as usual.
            await next(context);
            return;
        }

        var ct = context.RequestAborted;
        if (!context.Request.Headers.TryGetValue(HeaderName, out var values) || values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
        {
            await WriteProblemAsync(context, problems, ErrorCodes.IdempotencyKeyMissing,
                $"Send an {HeaderName} header (for example a UUID) so the request can be retried safely.");
            return;
        }

        var key = values[0]!.Trim();
        if (key.Length > IdempotencyRecord.KeyMaxLength || !key.All(c => c is >= '!' and <= '~'))
        {
            await WriteProblemAsync(context, problems, ErrorCodes.IdempotencyKeyInvalid,
                $"{HeaderName} must be 1 to {IdempotencyRecord.KeyMaxLength} printable ASCII characters without spaces.");
            return;
        }

        var customerId = context.User.CustomerId();
        var requestHash = await HashRequestAsync(context.Request, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            CustomerId = customerId,
            Key = key,
            RequestHash = requestHash,
            CreatedAt = clock.GetUtcNow(),
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();

            var existing = await db.IdempotencyRecords.AsNoTracking()
                .SingleAsync(r => r.CustomerId == customerId && r.Key == key, ct);

            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            {
                await WriteProblemAsync(context, problems, ErrorCodes.IdempotencyKeyReused,
                    $"This {HeaderName} was already used for a different request. Use a new key for a new operation.");
                return;
            }

            await ReplayAsync(context, existing, ct);
            return;
        }

        // The row is updated with ExecuteUpdate later; keep it out of the change tracker the endpoint uses.
        db.ChangeTracker.Clear();

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        var status = context.Response.StatusCode;
        if (!IsWorthStoring(status))
        {
            // Nothing happened (bad input, missing account): forget the key so a corrected request can reuse it.
            await transaction.RollbackAsync(CancellationToken.None);
            await CopyAsync(buffer, originalBody, ct);
            return;
        }

        try
        {
            var body = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            var contentType = context.Response.ContentType;
            var location = context.Response.Headers.Location.ToString();

            // Not cancellable: once the money moved, the outcome must be stored even if the client went away.
            await db.IdempotencyRecords
                .Where(r => r.CustomerId == customerId && r.Key == key)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(r => r.ResponseStatus, status)
                    .SetProperty(r => r.ResponseContentType, contentType)
                    .SetProperty(r => r.ResponseLocation, string.IsNullOrEmpty(location) ? null : location)
                    .SetProperty(r => r.ResponseBody, body), CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The response would claim an outcome that was never saved. Say so instead; nothing was committed.
            logger.LogError(ex, "Could not commit request with idempotency key for customer {CustomerId}", customerId);
            context.Response.Headers.Location = default;
            context.Response.ContentLength = null;
            await WriteProblemAsync(context, problems, ErrorCodes.ProcessingError,
                "The operation could not be completed and nothing was changed. It is safe to retry with the same key.");
            return;
        }

        await CopyAsync(buffer, originalBody, ct);
    }

    /// <summary>Responses where the request was understood and acted on. 400, 401, 403 and 404 mean nothing happened.</summary>
    private static bool IsWorthStoring(int status) =>
        status is not (StatusCodes.Status400BadRequest or StatusCodes.Status401Unauthorized
            or StatusCodes.Status403Forbidden or StatusCodes.Status404NotFound);

    private static async Task<string> HashRequestAsync(HttpRequest request, CancellationToken ct)
    {
        request.EnableBuffering();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{request.Method}\n{request.Path}\n"));

        var chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
        {
            hash.AppendData(chunk, 0, read);
        }

        request.Body.Position = 0;
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static async Task ReplayAsync(HttpContext context, IdempotencyRecord record, CancellationToken ct)
    {
        context.Response.StatusCode = record.ResponseStatus ?? StatusCodes.Status500InternalServerError;
        if (record.ResponseContentType is not null)
        {
            context.Response.ContentType = record.ResponseContentType;
        }

        if (record.ResponseLocation is not null)
        {
            context.Response.Headers.Location = record.ResponseLocation;
        }

        context.Response.Headers[ReplayedHeaderName] = "true";
        await context.Response.WriteAsync(record.ResponseBody ?? string.Empty, ct);
    }

    private static async Task CopyAsync(MemoryStream buffer, Stream destination, CancellationToken ct)
    {
        buffer.Position = 0;
        await buffer.CopyToAsync(destination, ct);
    }

    private static async Task WriteProblemAsync(HttpContext context, IProblemDetailsService problems, string code, string detail)
    {
        var status = ErrorStatuses.StatusFor(code);
        context.Response.StatusCode = status;
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = ReasonPhrases.GetReasonPhrase(status),
                Detail = detail,
                Extensions = { ["code"] = code },
            },
        });
    }
}
