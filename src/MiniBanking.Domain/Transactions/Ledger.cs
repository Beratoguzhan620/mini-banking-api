using MiniBanking.Domain.Accounts;

namespace MiniBanking.Domain.Transactions;

/// <summary>
/// Double-entry posting. Every movement — deposit, withdrawal or transfer — is "from one account to
/// another": deposits come from the currency's system account, withdrawals go to it. Each posting
/// writes exactly two entries that sum to zero.
/// </summary>
public static class Ledger
{
    public static IReadOnlyList<LedgerEntry> Move(BankTransaction transaction, Account from, Account to)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        if (from.Id == to.Id)
        {
            throw new DomainException(ErrorCodes.SameAccount, "Source and destination must be different accounts.");
        }

        if (from.Currency != transaction.Currency || to.Currency != transaction.Currency)
        {
            throw new DomainException(ErrorCodes.CurrencyMismatch,
                $"Both accounts must be in {transaction.Currency}. Currency conversion is not supported.");
        }

        var amount = transaction.AmountMinor;
        from.EnsureCanSend(amount);
        to.EnsureCanReceive();

        from.Debit(amount);
        to.Credit(amount);

        return
        [
            LedgerEntry.Create(transaction.Id, from.Id, -amount, BalanceAfter(from), transaction.CreatedAt),
            LedgerEntry.Create(transaction.Id, to.Id, amount, BalanceAfter(to), transaction.CreatedAt),
        ];
    }

    private static long? BalanceAfter(Account account) =>
        account.Kind == AccountKind.Customer ? account.BalanceMinor : null;
}
