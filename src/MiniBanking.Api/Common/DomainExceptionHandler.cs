using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using MiniBanking.Api.Features.Transactions;
using MiniBanking.Domain;

namespace MiniBanking.Api.Common;

/// <summary>
/// Turns business rule violations into RFC 9457 problem responses with a stable <c>code</c> field.
/// Anything else falls through to the default handler (500 without details).
/// </summary>
internal sealed class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (code, detail) = exception switch
        {
            DomainException domain => (domain.Code, domain.Message),
            DbUpdateConcurrencyException => (ErrorCodes.ConcurrentUpdate, "The resource was changed by another request. Read it again and retry."),
            _ => (null, null),
        };

        if (code is null)
        {
            return false;
        }

        var status = ErrorStatuses.StatusFor(code);
        httpContext.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = ReasonPhrases.GetReasonPhrase(status),
            Detail = detail,
            Extensions = { ["code"] = code },
        };

        if (exception is TransactionFailedException failed)
        {
            // The attempt is stored; the id lets the client and support find it.
            problem.Extensions["transactionId"] = failed.TransactionId;
        }

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }
}
