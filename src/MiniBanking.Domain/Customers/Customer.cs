namespace MiniBanking.Domain.Customers;

public sealed class Customer
{
    public const int EmailMaxLength = 254;
    public const int FullNameMaxLength = 120;

    private Customer()
    {
        Email = string.Empty;
        FullName = string.Empty;
        PasswordHash = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>Lower-cased and trimmed, so uniqueness does not depend on how it was typed.</summary>
    public string Email { get; private set; }

    public string FullName { get; private set; }

    public string PasswordHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static Customer Register(string email, string fullName, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        Email = NormalizeEmail(email),
        FullName = fullName.Trim(),
        CreatedAt = now,
    };

    public void SetPasswordHash(string hash) => PasswordHash = hash;
}
