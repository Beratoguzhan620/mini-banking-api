using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
    private const string PostingSavepoint = "posting";

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

    /// <summary>
    /// Both accounts are locked in id order. Two transfers running in opposite directions (A→B and B→A)
    /// therefore always request the locks in the same order and cannot deadlock.
    /// </summary>
    public Task<BankTransaction> TransferAsync(
        Guid customerId, Guid sourceAccountId, string destinationNumber, long amountMinor, string? description, CancellationToken ct) =>
        PostAsync(async () =>
        {
            var destinationId = await db.Accounts.AsNoTracking()
                .Where(a => a.Number == destinationNumber && a.Kind == AccountKind.Customer)
                .Select(a => (Guid?)a.Id)
                .SingleOrDefaultAsync(ct)
                ?? throw new DomainException(ErrorCodes.DestinationNotFound, "No account exists with this number.");

            var locked = new Dictionary<Guid, Account>();
            foreach (var id in new[] { sourceAccountId, destinationId }.Distinct().Order())
            {
                foreach (var account in await LockAsync(id, ct))
                {
                    locked[account.Id] = account;
                }
            }

            if (!locked.TryGetValue(sourceAccountId, out var source) || source.CustomerId != customerId || source.Kind != AccountKind.Customer)
            {
                throw new DomainException(ErrorCodes.NotFound, "Account not found.");
            }

            var destination = locked[destinationId];
            var transaction = BankTransaction.Transfer(customerId, source, destination, amountMinor, description, clock.GetUtcNow());
            return new Posting(transaction, source, destination);
        }, ct);

    private async Task<BankTransaction> PostAsync(Func<Task<Posting>> prepare, CancellationToken ct)
    {
        // When the request carries an idempotency key, the middleware has already opened a transaction that
        // also holds the key row. It then owns the commit, so the key and the money move together or not at all.
        var ambient = db.Database.CurrentTransaction;
        var owned = ambient is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var dbTransaction = ambient ?? owned!;

        try
        {
            return await PostInTransactionAsync(dbTransaction, commit: owned is not null, prepare, ct);
        }
        finally
        {
            if (owned is not null)
            {
                await owned.DisposeAsync();
            }
        }
    }

    private async Task<BankTransaction> PostInTransactionAsync(
        IDbContextTransaction dbTransaction, bool commit, Func<Task<Posting>> prepare, CancellationToken ct)
    {
        // Lookups, locks and ownership checks happen here; a missing account is a plain 404, not a failed transaction.
        var posting = await prepare();
        var transaction = posting.Transaction;
        await dbTransaction.CreateSavepointAsync(PostingSavepoint, ct);

        Exception failure;
        try
        {
            var entries = Ledger.Move(transaction, posting.From, posting.To);
            db.BankTransactions.Add(transaction);
            db.LedgerEntries.AddRange(entries);
            await db.SaveChangesAsync(ct);
            if (commit)
            {
                await dbTransaction.CommitAsync(ct);
            }

            return transaction;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failure = ex;
        }

        // Undo the posting but keep the account locks: the failed record below references both accounts,
        // and its foreign key checks must not queue behind other transfers. Writing it after a full rollback
        // took those row locks in constraint order instead of id order and deadlocked under load.
        await dbTransaction.RollbackToSavepointAsync(PostingSavepoint, ct);
        db.ChangeTracker.Clear();

        var reason = failure as DomainException
            ?? new DomainException(ErrorCodes.ProcessingError, "The operation could not be completed and nothing was changed.");
        transaction.Fail(reason.Code, reason.Message);
        db.BankTransactions.Add(transaction);
        await db.SaveChangesAsync(ct);
        if (commit)
        {
            await dbTransaction.CommitAsync(ct);
        }

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
