using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MiniBanking.Api.Common;

/// <summary>The landing text of the API reference: what the API is and how to call it.</summary>
internal sealed class DocumentInfoTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Mini Banking API",
            Version = "1.0",
            Description = """
                A small banking API built for correctness: a double-entry ledger, idempotent requests and
                transfers that stay consistent under concurrency.

                **Getting started**

                1. `POST /api/auth/register`, then `POST /api/auth/login` and paste the `accessToken` into *Authentication*.
                2. `POST /api/accounts` with a currency (`TRY`, `USD` or `EUR`).
                3. Deposit, withdraw or transfer. These need an `Idempotency-Key` header: any unique value per operation,
                   for example a UUID. Sending the same request again with the same key returns the first response.

                **Errors** follow RFC 9457 problem details with a stable `code` field, for example
                `insufficient_funds`, `currency_mismatch` or `idempotency_key_reused`. Rejected money movements are kept
                as `Failed` transactions and their id is returned as `transactionId`.

                Every response carries an `X-Correlation-Id`; send your own to follow a request through the logs.
                """,
            License = new OpenApiLicense { Name = "MIT", Url = new Uri("https://github.com/Beratoguzhan620/mini-banking-api/blob/main/LICENSE") },
        };

        return Task.CompletedTask;
    }
}
