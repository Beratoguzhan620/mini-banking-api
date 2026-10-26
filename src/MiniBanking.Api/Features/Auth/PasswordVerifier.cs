using Microsoft.AspNetCore.Identity;
using MiniBanking.Domain.Customers;

namespace MiniBanking.Api.Features.Auth;

/// <summary>
/// Wraps ASP.NET Core Identity's PBKDF2 password hasher. Unknown emails are checked against a dummy
/// hash so a login for a missing account takes as long as one with a wrong password.
/// </summary>
internal sealed class PasswordVerifier
{
    private static readonly Customer Dummy = Customer.Register("dummy@invalid", "Dummy", DateTimeOffset.UnixEpoch);

    private readonly PasswordHasher<Customer> _hasher = new();
    private readonly string _dummyHash;

    public PasswordVerifier()
    {
        _dummyHash = _hasher.HashPassword(Dummy, Guid.NewGuid().ToString());
    }

    public string Hash(Customer customer, string password) => _hasher.HashPassword(customer, password);

    /// <returns><c>true</c> when the password matches; <paramref name="rehash"/> is set when the stored hash uses outdated parameters.</returns>
    public bool Verify(Customer? customer, string password, out string? rehash)
    {
        rehash = null;
        if (customer is null)
        {
            _hasher.VerifyHashedPassword(Dummy, _dummyHash, password);
            return false;
        }

        var result = _hasher.VerifyHashedPassword(customer, customer.PasswordHash, password);
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            rehash = _hasher.HashPassword(customer, password);
        }

        return result != PasswordVerificationResult.Failed;
    }
}
