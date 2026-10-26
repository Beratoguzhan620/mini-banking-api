using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MiniBanking.Domain.Customers;

namespace MiniBanking.Api.Features.Auth;

internal sealed record AccessTokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

internal sealed class TokenService(SigningKeyProvider keys, IOptions<JwtOptions> options, TimeProvider clock)
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessTokenResponse Issue(Customer customer)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow();
        var expiresAt = now.AddMinutes(settings.AccessTokenMinutes);

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(keys.Key, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = customer.Id.ToString(),
                [JwtRegisteredClaimNames.Email] = customer.Email,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            },
        });

        return new AccessTokenResponse(token, "Bearer", expiresAt);
    }
}
