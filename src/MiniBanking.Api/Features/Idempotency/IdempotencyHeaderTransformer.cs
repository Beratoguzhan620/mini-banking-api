using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using MiniBanking.Infrastructure.Persistence;

namespace MiniBanking.Api.Features.Idempotency;

/// <summary>Documents the required header on every endpoint that needs it, so Scalar shows the field.</summary>
internal sealed class IdempotencyHeaderTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.Description.ActionDescriptor.EndpointMetadata.OfType<RequiresIdempotencyKey>().Any())
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = IdempotencyMiddleware.HeaderName,
                In = ParameterLocation.Header,
                Required = true,
                Description = "Any unique value per operation, for example a UUID. Retrying with the same key returns the first response instead of moving money again.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = IdempotencyRecord.KeyMaxLength },
            });
        }

        return Task.CompletedTask;
    }
}
