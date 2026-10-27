using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using MiniBanking.Api.Common;
using MiniBanking.Domain;
using MiniBanking.Domain.Accounts;
using MiniBanking.Domain.Audit;
using MiniBanking.Infrastructure.Persistence;

namespace MiniBanking.Api.Features.Operations;

internal sealed record CurrencyPosition(string Currency, decimal CustomerBalances, decimal BankPosition, bool Balanced);

internal sealed record DriftedAccount(Guid AccountId, string AccountNumber, decimal CachedBalance, decimal LedgerBalance);

internal sealed record ReconciliationReport(
    DateTimeOffset CheckedAt,
    bool Healthy,
    int CustomerAccounts,
    int UnbalancedTransactions,
    IReadOnlyList<DriftedAccount> DriftedAccounts,
    IReadOnlyList<CurrencyPosition> Currencies);

internal sealed record AuditEntryResponse(
    long Id, DateTimeOffset OccurredAt, Guid? ActorId, string Action, string TargetType, string? TargetId,
    AuditOutcome Outcome, string? Details, string? CorrelationId);

internal sealed record AuditPage(IReadOnlyList<AuditEntryResponse> Items, string? NextCursor);

/// <summary>Back-office endpoints, protected by the operations key rather than a customer token.</summary>
internal static class OperationsEndpoints
{
    private const int MaxDriftedShown = 100;
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    public static IEndpointRouteBuilder MapOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ops").WithTags("Operations").AddEndpointFilter<OperationsKeyFilter>();
        group.MapGet("/reconciliation", Reconcile).WithSummary("Compare every cached balance with the ledger");
        group.MapGet("/audit", Audit).WithSummary("Search the audit trail, newest first");
        return app;
    }

    private static async Task<Ok<ReconciliationReport>> Reconcile(BankingDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var customers = db.Accounts.AsNoTracking().Where(a => a.Kind == AccountKind.Customer);

        var customerAccounts = await customers.CountAsync(ct);

        var unbalanced = await db.LedgerEntries
            .GroupBy(e => e.TransactionId)
            .Where(g => g.Sum(e => e.AmountMinor) != 0)
            .CountAsync(ct);

        var drifted = await customers
            .Select(a => new
            {
                a.Id,
                a.Number,
                a.BalanceMinor,
                Ledger = db.LedgerEntries.Where(e => e.AccountId == a.Id).Sum(e => (long?)e.AmountMinor) ?? 0,
            })
            .Where(x => x.BalanceMinor != x.Ledger)
            .OrderBy(x => x.Id)
            .Take(MaxDriftedShown)
            .ToListAsync(ct);

        var customerTotals = await customers
            .GroupBy(a => a.Currency)
            .Select(g => new { Currency = g.Key, Total = g.Sum(a => a.BalanceMinor) })
            .ToDictionaryAsync(x => x.Currency, x => x.Total, ct);

        // The system account's ledger is the mirror image of every deposit and withdrawal.
        var systemTotals = await db.LedgerEntries
            .Join(db.Accounts.Where(a => a.Kind == AccountKind.System), e => e.AccountId, a => a.Id, (e, a) => new { a.Currency, e.AmountMinor })
            .GroupBy(x => x.Currency)
            .Select(g => new { Currency = g.Key, Total = g.Sum(x => x.AmountMinor) })
            .ToDictionaryAsync(x => x.Currency, x => x.Total, ct);

        var positions = SystemAccounts.All.Keys.Order(StringComparer.Ordinal)
            .Select(currency =>
            {
                var customerTotal = customerTotals.GetValueOrDefault(currency);
                var bankPosition = -systemTotals.GetValueOrDefault(currency);
                return new CurrencyPosition(currency, Amounts.ToMajor(customerTotal), Amounts.ToMajor(bankPosition), customerTotal == bankPosition);
            })
            .ToList();

        var report = new ReconciliationReport(
            clock.GetUtcNow(),
            unbalanced == 0 && drifted.Count == 0 && positions.All(p => p.Balanced),
            customerAccounts,
            unbalanced,
            drifted.Select(d => new DriftedAccount(d.Id, d.Number, Amounts.ToMajor(d.BalanceMinor), Amounts.ToMajor(d.Ledger))).ToList(),
            positions);

        return TypedResults.Ok(report);
    }

    private static async Task<Results<Ok<AuditPage>, ValidationProblem>> Audit(
        BankingDbContext db, CancellationToken ct,
        Guid? actorId = null, string? targetId = null, string? action = null, string? correlationId = null,
        int? limit = null, string? cursor = null)
    {
        long cursorId = 0;
        var validation = new Validation()
            .Require(limit is null or (>= 1 and <= MaxPageSize), "limit", $"Limit must be between 1 and {MaxPageSize}.")
            .Require(cursor is null || TryDecodeCursor(cursor, out cursorId), "cursor", "The cursor is not valid.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var query = db.AuditEntries.AsNoTracking();
        if (actorId is { } actor)
        {
            query = query.Where(e => e.ActorId == actor);
        }

        if (!string.IsNullOrWhiteSpace(targetId))
        {
            query = query.Where(e => e.TargetId == targetId);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(e => e.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            query = query.Where(e => e.CorrelationId == correlationId);
        }

        if (cursor is not null)
        {
            query = query.Where(e => e.Id < cursorId);
        }

        var pageSize = limit ?? DefaultPageSize;
        var rows = await query.OrderByDescending(e => e.Id).Take(pageSize + 1).ToListAsync(ct);
        var page = rows.Take(pageSize).ToList();

        var items = page
            .Select(e => new AuditEntryResponse(e.Id, e.OccurredAt, e.ActorId, e.Action, e.TargetType, e.TargetId, e.Outcome, e.Details, e.CorrelationId))
            .ToList();
        var next = rows.Count > pageSize
            ? WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(page[^1].Id.ToString(CultureInfo.InvariantCulture)))
            : null;

        return TypedResults.Ok(new AuditPage(items, next));
    }

    private static bool TryDecodeCursor(string cursor, out long id)
    {
        id = 0;
        try
        {
            var text = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor));
            return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
