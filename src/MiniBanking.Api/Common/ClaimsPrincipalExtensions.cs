using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace MiniBanking.Api.Common;

internal static class ClaimsPrincipalExtensions
{
    /// <summary>The authenticated customer. Endpoints that call this require authorization, so the claim is always present.</summary>
    public static Guid CustomerId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new InvalidOperationException("The token has no subject claim."));
}
