using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MiniBanking.Api.Common;
using MiniBanking.Api.Features.Audit;
using MiniBanking.Domain;
using MiniBanking.Domain.Accounts;
using MiniBanking.Domain.Audit;
using MiniBanking.Infrastructure.Persistence;

namespace MiniBanking.Api.Features.Accounts;

/// <param name="Currency">TRY, USD or EUR.</param>
internal sealed record OpenAccountRequest(string Currency);

internal sealed record AccountResponse(
    Guid Id,
    string Number,
    string Currency,
    decimal Balance,
    AccountStatus Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt)
{
    public static AccountResponse From(Account account) => new(
        account.Id,
        account.Number,
        account.Currency,
        (decimal)account.BalanceMinor / Currencies.MinorUnitsPerMajor,
        account.Status,
        account.OpenedAt,
        account.ClosedAt);
}

internal static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/accounts").WithTags("Accounts").RequireAuthorization();

        group.MapGet("/", List).WithSummary("List your accounts");
        group.MapPost("/", Open).WithSummary("Open an account in a currency");
        group.MapGet("/{id:guid}", Get).WithSummary("Get one of your accounts");
        group.MapPost("/{id:guid}/freeze", Freeze).WithSummary("Freeze an account: no money can leave it");
        group.MapPost("/{id:guid}/unfreeze", Unfreeze).WithSummary("Unfreeze an account");
        group.MapPost("/{id:guid}/close", Close).WithSummary("Close an account with a zero balance");

        return app;
    }

    private static async Task<Ok<List<AccountResponse>>> List(ClaimsPrincipal user, BankingDbContext db, CancellationToken ct)
    {
        var customerId = user.CustomerId();
        var accounts = await db.Accounts.AsNoTracking()
            .Where(a => a.CustomerId == customerId)
            .OrderBy(a => a.OpenedAt)
            .ToListAsync(ct);

        return TypedResults.Ok(accounts.Select(AccountResponse.From).ToList());
    }

    private static async Task<Results<Created<AccountResponse>, ValidationProblem>> Open(
        OpenAccountRequest request, ClaimsPrincipal user, BankingDbContext db, AuditTrail audit, TimeProvider clock, CancellationToken ct)
    {
        var validation = new Validation()
            .Require(!string.IsNullOrWhiteSpace(request.Currency), "currency",
                $"Currency is required. Supported: {string.Join(", ", Currencies.Supported)}.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var account = Account.Open(user.CustomerId(), request.Currency, clock.GetUtcNow());
        db.Accounts.Add(account);
        audit.Record("account.opened", "account", account.Id, AuditOutcome.Succeeded, new { account.Currency });
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/accounts/{account.Id}", AccountResponse.From(account));
    }

    private static async Task<Ok<AccountResponse>> Get(Guid id, ClaimsPrincipal user, BankingDbContext db, CancellationToken ct) =>
        TypedResults.Ok(AccountResponse.From(await db.FindOwnedAccountAsync(id, user.CustomerId(), ct)));

    private static Task<Ok<AccountResponse>> Freeze(Guid id, ClaimsPrincipal user, BankingDbContext db, AuditTrail audit, CancellationToken ct) =>
        Change(id, user, db, audit, "account.frozen", account => account.Freeze(), ct);

    private static Task<Ok<AccountResponse>> Unfreeze(Guid id, ClaimsPrincipal user, BankingDbContext db, AuditTrail audit, CancellationToken ct) =>
        Change(id, user, db, audit, "account.unfrozen", account => account.Unfreeze(), ct);

    private static Task<Ok<AccountResponse>> Close(
        Guid id, ClaimsPrincipal user, BankingDbContext db, AuditTrail audit, TimeProvider clock, CancellationToken ct) =>
        Change(id, user, db, audit, "account.closed", account => account.Close(clock.GetUtcNow()), ct);

    private static async Task<Ok<AccountResponse>> Change(
        Guid id, ClaimsPrincipal user, BankingDbContext db, AuditTrail audit, string action, Action<Account> change, CancellationToken ct)
    {
        var account = await db.FindOwnedAccountAsync(id, user.CustomerId(), ct);
        change(account);
        audit.Record(action, "account", account.Id, AuditOutcome.Succeeded);
        // xmin makes this fail with 409 if a transfer touched the account in between.
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(AccountResponse.From(account));
    }
}

internal static class AccountQueries
{
    /// <summary>
    /// Someone else's account answers exactly like a missing one, so ids cannot be probed.
    /// </summary>
    public static async Task<Account> FindOwnedAccountAsync(this BankingDbContext db, Guid id, Guid customerId, CancellationToken ct) =>
        await db.Accounts.SingleOrDefaultAsync(a => a.Id == id && a.CustomerId == customerId && a.Kind == AccountKind.Customer, ct)
        ?? throw new DomainException(ErrorCodes.NotFound, "Account not found.");
}
