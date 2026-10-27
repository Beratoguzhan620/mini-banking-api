using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using MiniBanking.Api.Common;
using MiniBanking.Api.Features.Idempotency;
using MiniBanking.Api.Features.Accounts;
using MiniBanking.Domain;
using MiniBanking.Domain.Accounts;
using MiniBanking.Domain.Transactions;
using MiniBanking.Infrastructure.Persistence;

namespace MiniBanking.Api.Features.Transactions;

internal sealed record MoneyRequest(decimal? Amount, string? Description);

internal sealed record TransactionResponse(
    Guid Id,
    TransactionType Type,
    TransactionStatus Status,
    string Currency,
    decimal Amount,
    string? SourceAccountNumber,
    string? DestinationAccountNumber,
    string? Description,
    string? FailureCode,
    DateTimeOffset CreatedAt);

/// <param name="Amount">Signed from this account's point of view: negative when money left it.</param>
internal sealed record HistoryItem(
    Guid TransactionId,
    TransactionType Type,
    decimal Amount,
    decimal BalanceAfter,
    string? CounterpartyAccountNumber,
    string? Description,
    DateTimeOffset CreatedAt);

internal sealed record HistoryPage(IReadOnlyList<HistoryItem> Items, string? NextCursor);

internal static class TransactionEndpoints
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        var accounts = app.MapGroup("/api/accounts/{id:guid}").WithTags("Transactions").RequireAuthorization();
        accounts.MapPost("/deposits", Deposit).WithSummary("Deposit money (simulated)").RequireIdempotencyKey();
        accounts.MapPost("/withdrawals", Withdraw).WithSummary("Withdraw money (simulated)").RequireIdempotencyKey();
        accounts.MapGet("/transactions", History).WithSummary("Account history, newest first, keyset paginated");

        app.MapGet("/api/transactions/{id:guid}", Get)
            .WithTags("Transactions").WithSummary("A transaction you started or that touched your account").RequireAuthorization();

        return app;
    }

    private static Task<Results<Created<TransactionResponse>, ValidationProblem>> Deposit(
        Guid id, MoneyRequest request, ClaimsPrincipal user, MoneyMovementService money, BankingDbContext db, CancellationToken ct) =>
        Move(request, db, user, ct, (customerId, amount) => money.DepositAsync(customerId, id, amount, request.Description, ct));

    private static Task<Results<Created<TransactionResponse>, ValidationProblem>> Withdraw(
        Guid id, MoneyRequest request, ClaimsPrincipal user, MoneyMovementService money, BankingDbContext db, CancellationToken ct) =>
        Move(request, db, user, ct, (customerId, amount) => money.WithdrawAsync(customerId, id, amount, request.Description, ct));

    internal static async Task<Results<Created<TransactionResponse>, ValidationProblem>> Move(
        MoneyRequest request, BankingDbContext db, ClaimsPrincipal user, CancellationToken ct,
        Func<Guid, long, Task<BankTransaction>> execute, Validation? validation = null)
    {
        long amountMinor = 0;
        validation ??= new Validation();
        validation
            .Require(request.Amount is { } amount && Amounts.TryToMinor(amount, out amountMinor), "amount",
                $"Amount must be positive, have at most two decimals and be no more than {Amounts.MaxPerOperation:N0}.")
            .Require(request.Description is null || request.Description.Length <= BankTransaction.DescriptionMaxLength,
                "description", $"Description can be at most {BankTransaction.DescriptionMaxLength} characters.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var transaction = await execute(user.CustomerId(), amountMinor);
        var response = await ToResponseAsync(db, transaction, ct);
        return TypedResults.Created($"/api/transactions/{transaction.Id}", response);
    }

    private static async Task<Ok<TransactionResponse>> Get(Guid id, ClaimsPrincipal user, BankingDbContext db, CancellationToken ct)
    {
        var customerId = user.CustomerId();
        var transaction = await db.BankTransactions.AsNoTracking()
            .Where(t => t.Id == id)
            .Where(t => t.InitiatedBy == customerId
                || db.Accounts.Any(a => a.CustomerId == customerId && (a.Id == t.SourceAccountId || a.Id == t.DestinationAccountId)))
            .SingleOrDefaultAsync(ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "Transaction not found.");

        return TypedResults.Ok(await ToResponseAsync(db, transaction, ct));
    }

    private static async Task<Results<Ok<HistoryPage>, ValidationProblem>> History(
        Guid id, ClaimsPrincipal user, BankingDbContext db, CancellationToken ct,
        DateTimeOffset? from = null, DateTimeOffset? to = null, string? type = null, int? limit = null, string? cursor = null)
    {
        TransactionType parsedType = default;
        long cursorId = 0;
        var validation = new Validation()
            .Require(limit is null or (>= 1 and <= MaxPageSize), "limit", $"Limit must be between 1 and {MaxPageSize}.")
            .Require(from is null || to is null || from < to, "to", "'to' must be later than 'from'.")
            .Require(type is null || Enum.TryParse(type, ignoreCase: true, out parsedType), "type",
                $"Type must be one of: {string.Join(", ", Enum.GetNames<TransactionType>())}.")
            .Require(cursor is null || TryDecodeCursor(cursor, out cursorId), "cursor", "The cursor is not valid.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        await db.FindOwnedAccountAsync(id, user.CustomerId(), ct);

        var query = db.LedgerEntries.AsNoTracking()
            .Where(entry => entry.AccountId == id)
            .Join(db.BankTransactions, entry => entry.TransactionId, transaction => transaction.Id,
                (entry, transaction) => new { Entry = entry, Transaction = transaction });

        if (from is { } fromUtc)
        {
            var start = fromUtc.ToUniversalTime();
            query = query.Where(x => x.Entry.CreatedAt >= start);
        }

        if (to is { } toUtc)
        {
            var end = toUtc.ToUniversalTime();
            query = query.Where(x => x.Entry.CreatedAt < end);
        }

        if (type is not null)
        {
            query = query.Where(x => x.Transaction.Type == parsedType);
        }

        if (cursor is not null)
        {
            query = query.Where(x => x.Entry.Id < cursorId);
        }

        var pageSize = limit ?? DefaultPageSize;
        var rows = await query
            .OrderByDescending(x => x.Entry.Id)
            .Take(pageSize + 1)
            .Select(x => new
            {
                EntryId = x.Entry.Id,
                TransactionId = x.Transaction.Id,
                x.Transaction.Type,
                x.Entry.AmountMinor,
                x.Entry.BalanceAfterMinor,
                x.Transaction.Description,
                x.Entry.CreatedAt,
                Counterparty = db.LedgerEntries
                    .Where(other => other.TransactionId == x.Entry.TransactionId && other.AccountId != x.Entry.AccountId)
                    .Join(db.Accounts, other => other.AccountId, account => account.Id, (other, account) => account)
                    .Where(account => account.Kind == AccountKind.Customer)
                    .Select(account => account.Number)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        var page = rows.Take(pageSize).ToList();
        var items = page
            .Select(r => new HistoryItem(r.TransactionId, r.Type, Amounts.ToMajor(r.AmountMinor), Amounts.ToMajor(r.BalanceAfterMinor ?? 0),
                r.Counterparty, r.Description, r.CreatedAt))
            .ToList();

        return TypedResults.Ok(new HistoryPage(items, hasMore ? EncodeCursor(page[^1].EntryId) : null));
    }

    internal static async Task<TransactionResponse> ToResponseAsync(BankingDbContext db, BankTransaction transaction, CancellationToken ct)
    {
        var numbers = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == transaction.SourceAccountId || a.Id == transaction.DestinationAccountId)
            .ToDictionaryAsync(a => a.Id, a => a.Number, ct);

        return new TransactionResponse(
            transaction.Id,
            transaction.Type,
            transaction.Status,
            transaction.Currency,
            Amounts.ToMajor(transaction.AmountMinor),
            transaction.SourceAccountId is { } source ? numbers.GetValueOrDefault(source) : null,
            transaction.DestinationAccountId is { } destination ? numbers.GetValueOrDefault(destination) : null,
            transaction.Description,
            transaction.FailureCode,
            transaction.CreatedAt);
    }

    // The cursor is opaque to clients; today it is the last ledger entry id they saw.
    private static string EncodeCursor(long entryId) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(entryId.ToString(CultureInfo.InvariantCulture)));

    private static bool TryDecodeCursor(string cursor, out long entryId)
    {
        entryId = 0;
        try
        {
            var text = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor));
            return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out entryId) && entryId > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
