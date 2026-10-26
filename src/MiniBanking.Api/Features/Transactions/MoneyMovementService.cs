using Microsoft.EntityFrameworkCore;
using MiniBanking.Domain;
using MiniBanking.Domain.Accounts;
using MiniBanking.Domain.Transactions;
using MiniBanking.Infrastructure.Persistence;

namespace MiniBanking.Api.Features.Transactions;

/// <summary>
/// Moves money. Each operation runs in one database transaction: lock the customer accounts involved,
/// check the rules, write the transaction and its two ledger entries, update cached balances, commit.
/// If a rule fails, nothing is kept except a <see cref="TransactionStatus.Failed"/> record written afterwards.
/// </summary>
internal sealed class MoneyMovementService(BankingDbContext db, TimeProvider clock, ILogger<MoneyMovementService> logger)
{
    private sealed record Posting(BankTransaction Transaction, Account From, Account To);

    public Task<BankTransaction> DepositAsync(Guid customerId, Guid accountId, long amountMinor, string? description, CancellationToken ct) =>
        PostAsync(async () =>
        {
            var account = await LockOwnedAsync(accountId, customerId, ct);
            var system = await SystemAccountAsync(account.Currency, ct);
            var transaction = BankTransaction.Deposit(customerId, account, amountMinor, description, clock.GetUtcNow());
            return new Posting(transaction, system, account);
        }, ct);

    public Task<BankTransaction> WithdrawAsync(Guid customerId, Guid accountId, long amountMinor, string? description, CancellationToken ct) =>
        PostAsync(async () =>
        {
            var account = await LockOwnedAsync(accountId, customerId, ct);
            var system = await SystemAccountAsync(account.Currency, ct);
            var transaction = BankTransaction.Withdrawal(customerId, account, amountMinor, description, clock.GetUtcNow());
            return new Posting(transaction, account, system);
        }, ct);

    private async Task<BankTransaction> PostAsync(Func<Task<Posting>> prepare, CancellationToken ct)
    {
        BankTransaction transaction;
        Exception failure;

        await using (var dbTransaction = await db.Database.BeginTransactionAsync(ct))
        {
            // Lookups and ownership checks happen here; a missing account is a plain 404, not a failed transaction.
            var posting = await prepare();
            transaction = posting.Transaction;

            try
            {
                var entries = Ledger.Move(transaction, posting.From, posting.To);
                db.BankTransactions.Add(transaction);
                db.LedgerEntries.AddRange(entries);
                await db.SaveChangesAsync(ct);
                await dbTransaction.CommitAsync(ct);
                return transaction;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failure = ex;
            }
        }

        // The database transaction is rolled back at this point; forget the in-memory balance changes too.
        db.ChangeTracker.Clear();

        var reason = failure as DomainException
            ?? new DomainException(ErrorCodes.ProcessingError, "The operation could not be completed and nothing was changed.");
        transaction.Fail(reason.Code, reason.Message);
        db.BankTransactions.Add(transaction);
        await db.SaveChangesAsync(ct);

        if (failure is not DomainException)
        {
            logger.LogError(failure, "Transaction {TransactionId} failed unexpectedly and was rolled back", transaction.Id);
        }

        throw new TransactionFailedException(transaction.Id, reason);
    }

    /// <summary>
    /// Locks the row for the rest of the database transaction. <c>xmin</c> is a system column, so it is
    /// selected explicitly for the concurrency token.
    /// </summary>
    private async Task<Account> LockOwnedAsync(Guid accountId, Guid customerId, CancellationToken ct)
    {
        var account = (await LockAsync(accountId, ct))
            .SingleOrDefault(a => a.CustomerId == customerId && a.Kind == AccountKind.Customer);
        return account ?? throw new DomainException(ErrorCodes.NotFound, "Account not found.");
    }

    private Task<List<Account>> LockAsync(Guid accountId, CancellationToken ct) =>
        db.Accounts.FromSql($"SELECT *, xmin FROM accounts WHERE id = {accountId} FOR UPDATE").ToListAsync(ct);

    private async Task<Account> SystemAccountAsync(string currency, CancellationToken ct) =>
        await db.Accounts.SingleOrDefaultAsync(a => a.Id == SystemAccounts.IdFor(currency), ct)
        ?? throw new InvalidOperationException($"The {currency} system account is missing; run the migrations.");
}
