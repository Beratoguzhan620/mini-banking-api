using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace MiniBanking.Api.Features.Operations;

internal sealed class OperationsKeyFilter(IOptions<OperationsOptions> options) : IEndpointFilter
{
    public const string HeaderName = "X-Operations-Key";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var expected = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(expected))
        {
            return TypedResults.NotFound();
        }

        var provided = context.HttpContext.Request.Headers[HeaderName].ToString();
        // Constant-time comparison, so response timing does not reveal how much of the key was right.
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected)))
        {
            return TypedResults.Unauthorized();
        }

        return await next(context);
    }
}
