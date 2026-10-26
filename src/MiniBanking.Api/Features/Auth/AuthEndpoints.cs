using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MiniBanking.Api.Common;
using MiniBanking.Domain;
using MiniBanking.Domain.Customers;
using MiniBanking.Infrastructure.Persistence;

namespace MiniBanking.Api.Features.Auth;

internal sealed record RegisterRequest(string? Email, string? FullName, string? Password);

internal sealed record LoginRequest(string? Email, string? Password);

internal sealed record CustomerResponse(Guid Id, string Email, string FullName, DateTimeOffset CreatedAt)
{
    public static CustomerResponse From(Customer customer) =>
        new(customer.Id, customer.Email, customer.FullName, customer.CreatedAt);
}

internal static class AuthEndpoints
{
    private const int PasswordMinLength = 10;
    private const int PasswordMaxLength = 128;

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");
        group.MapPost("/register", Register).WithSummary("Register a customer");
        group.MapPost("/login", Login).WithSummary("Exchange email and password for an access token");

        app.MapGet("/api/me", Me).WithTags("Auth").WithSummary("The signed-in customer").RequireAuthorization();
        return app;
    }

    private static async Task<Results<Created<CustomerResponse>, ValidationProblem>> Register(
        RegisterRequest request, BankingDbContext db, PasswordVerifier passwords, TimeProvider clock, CancellationToken ct)
    {
        var validation = new Validation()
            .Require(IsEmail(request.Email), "email", "A valid email address is required.")
            .Require(!string.IsNullOrWhiteSpace(request.FullName) && request.FullName.Trim().Length <= Customer.FullNameMaxLength,
                "fullName", $"Full name is required and can be at most {Customer.FullNameMaxLength} characters.")
            .Require(request.Password is { Length: >= PasswordMinLength and <= PasswordMaxLength },
                "password", $"Password must be {PasswordMinLength} to {PasswordMaxLength} characters.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var email = Customer.NormalizeEmail(request.Email!);
        if (await db.Customers.AnyAsync(c => c.Email == email, ct))
        {
            throw EmailTaken();
        }

        var customer = Customer.Register(email, request.FullName!, clock.GetUtcNow());
        customer.SetPasswordHash(passwords.Hash(customer, request.Password!));
        db.Customers.Add(customer);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // Two registrations for the same email raced past the check above.
            throw EmailTaken();
        }

        return TypedResults.Created("/api/me", CustomerResponse.From(customer));
    }

    private static async Task<Results<Ok<AccessTokenResponse>, ValidationProblem>> Login(
        LoginRequest request, BankingDbContext db, PasswordVerifier passwords, TokenService tokens, CancellationToken ct)
    {
        var validation = new Validation()
            .Require(!string.IsNullOrWhiteSpace(request.Email), "email", "Email is required.")
            .Require(!string.IsNullOrEmpty(request.Password), "password", "Password is required.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var email = Customer.NormalizeEmail(request.Email!);
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Email == email, ct);

        if (!passwords.Verify(customer, request.Password!, out var rehash) || customer is null)
        {
            // Same answer for an unknown email and a wrong password.
            throw new DomainException(ErrorCodes.InvalidCredentials, "Email or password is incorrect.");
        }

        if (rehash is not null)
        {
            customer.SetPasswordHash(rehash);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(tokens.Issue(customer));
    }

    private static async Task<Ok<CustomerResponse>> Me(ClaimsPrincipal user, BankingDbContext db, CancellationToken ct)
    {
        var customerId = user.CustomerId();
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == customerId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "Customer not found.");
        return TypedResults.Ok(CustomerResponse.From(customer));
    }

    private static bool IsEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length <= Customer.EmailMaxLength
        && MailAddress.TryCreate(value.Trim(), out var address)
        && string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase)
        && address.Host.Contains('.', StringComparison.Ordinal);

    private static DomainException EmailTaken() =>
        new(ErrorCodes.EmailTaken, "An account with this email already exists.");
}
