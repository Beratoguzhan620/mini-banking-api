namespace MiniBanking.Domain.Transactions;

/// <summary>One side of a money movement. Entries are only ever inserted, never updated or deleted.</summary>
public sealed class LedgerEntry
{
    private LedgerEntry()
    {
    }

    /// <summary>Database identity; also the stable order used for history paging.</summary>
    public long Id { get; private set; }

    public Guid TransactionId { get; private set; }

    public Guid AccountId { get; private set; }

    /// <summary>Signed: negative when money leaves the account.</summary>
    public long AmountMinor { get; private set; }

    /// <summary>Customer account balance right after this entry; <c>null</c> for system accounts.</summary>
    public long? BalanceAfterMinor { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    internal static LedgerEntry Create(Guid transactionId, Guid accountId, long amountMinor, long? balanceAfterMinor, DateTimeOffset now) => new()
    {
        TransactionId = transactionId,
        AccountId = accountId,
        AmountMinor = amountMinor,
        BalanceAfterMinor = balanceAfterMinor,
        CreatedAt = now,
    };
}
