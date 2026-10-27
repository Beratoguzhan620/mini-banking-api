using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using MiniBanking.Api.Features.Operations;

namespace MiniBanking.Api.Common;

/// <summary>
/// Declares the two ways to call the API: a customer's JWT and the back-office key. Each operation then
/// lists only the one it accepts, so Scalar asks for the right credential on each page.
/// </summary>
internal sealed class SecuritySchemesTransformer : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    private const string BearerScheme = "Bearer";
    private const string OperationsScheme = "OperationsKey";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste the accessToken from POST /api/auth/login.",
        };
        document.Components.SecuritySchemes[OperationsScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = OperationsKeyFilter.HeaderName,
            Description = "Back-office key (Operations:ApiKey). The endpoints answer 404 when it is not configured.",
        };

        return Task.CompletedTask;
    }

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var scheme = metadata.OfType<RequiresOperationsKey>().Any() ? OperationsScheme
            : metadata.OfType<IAuthorizeData>().Any() ? BearerScheme
            : null;

        if (scheme is not null)
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(scheme, context.Document)] = [],
            });
        }

        return Task.CompletedTask;
    }
}
