using MiniBanking.Domain.Accounts;

namespace MiniBanking.Domain.Transactions;

/// <summary>
/// One money movement as the customer sees it. The ledger entries it produces are the accounting
/// record; a failed attempt is kept here without any entries so it stays visible.
/// </summary>
public sealed class BankTransaction
{
    public const int DescriptionMaxLength = 140;

    private BankTransaction()
    {
        Currency = string.Empty;
    }

    public Guid Id { get; private set; }

    public TransactionType Type { get; private set; }

    public TransactionStatus Status { get; private set; }

    public string Currency { get; private set; }

    public long AmountMinor { get; private set; }

    /// <summary>Customer account the money left; <c>null</c> for deposits.</summary>
    public Guid? SourceAccountId { get; private set; }

    /// <summary>Customer account the money reached; <c>null</c> for withdrawals.</summary>
    public Guid? DestinationAccountId { get; private set; }

    public Guid InitiatedBy { get; private set; }

    public string? Description { get; private set; }

    public string? FailureCode { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static BankTransaction Deposit(Guid initiatedBy, Account destination, long amountMinor, string? description, DateTimeOffset now) =>
        Create(TransactionType.Deposit, initiatedBy, null, destination, amountMinor, description, now);

    public static BankTransaction Withdrawal(Guid initiatedBy, Account source, long amountMinor, string? description, DateTimeOffset now) =>
        Create(TransactionType.Withdrawal, initiatedBy, source, null, amountMinor, description, now);

    public static BankTransaction Transfer(Guid initiatedBy, Account source, Account destination, long amountMinor, string? description, DateTimeOffset now) =>
        Create(TransactionType.Transfer, initiatedBy, source, destination, amountMinor, description, now);

    public void Fail(string code, string reason)
    {
        Status = TransactionStatus.Failed;
        FailureCode = code;
        FailureReason = reason.Length > 300 ? reason[..300] : reason;
    }

    private static BankTransaction Create(
        TransactionType type, Guid initiatedBy, Account? source, Account? destination, long amountMinor, string? description, DateTimeOffset now)
    {
        if (amountMinor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amountMinor), "Amounts are always positive; direction comes from the ledger entries.");
        }

        var trimmed = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        return new BankTransaction
        {
            Id = Guid.CreateVersion7(now),
            Type = type,
            Status = TransactionStatus.Completed,
            Currency = (source ?? destination)!.Currency,
            AmountMinor = amountMinor,
            SourceAccountId = source?.Id,
            DestinationAccountId = destination?.Id,
            InitiatedBy = initiatedBy,
            Description = trimmed is { Length: > DescriptionMaxLength } ? trimmed[..DescriptionMaxLength] : trimmed,
            CreatedAt = now,
        };
    }
}
