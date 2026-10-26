namespace MiniBanking.Domain.Accounts;

public sealed class Account
{
    private Account()
    {
        Number = string.Empty;
        Currency = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>Owner; <c>null</c> only for system accounts.</summary>
    public Guid? CustomerId { get; private set; }

    public string Number { get; private set; }

    public string Currency { get; private set; }

    public AccountKind Kind { get; private set; }

    /// <summary>Balance in minor units (kuruş, cents). Not maintained for system accounts.</summary>
    public long BalanceMinor { get; private set; }

    public AccountStatus Status { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Concurrency token, mapped to PostgreSQL's <c>xmin</c> system column.</summary>
    public uint Version { get; private set; }

    public static Account Open(Guid customerId, string currency, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        CustomerId = customerId,
        Number = AccountNumbers.Generate(),
        Currency = Currencies.Normalize(currency),
        Kind = AccountKind.Customer,
        Status = AccountStatus.Active,
        OpenedAt = now,
    };

    /// <summary>The bank's side of deposits and withdrawals for one currency. Seeded by a migration.</summary>
    public static Account OpenSystem(string currency, DateTimeOffset openedAt) => new()
    {
        Id = SystemAccounts.IdFor(currency),
        Number = SystemAccounts.NumberFor(currency),
        Currency = currency,
        Kind = AccountKind.System,
        Status = AccountStatus.Active,
        OpenedAt = openedAt,
    };

    /// <summary>Checks that <paramref name="amountMinor"/> may leave this account.</summary>
    public void EnsureCanSend(long amountMinor)
    {
        EnsureNotClosed();
        if (Kind == AccountKind.System)
        {
            return;
        }

        if (Status == AccountStatus.Frozen)
        {
            throw new DomainException(ErrorCodes.AccountFrozen, "The account is frozen; money cannot leave it.");
        }

        if (BalanceMinor < amountMinor)
        {
            throw new DomainException(ErrorCodes.InsufficientFunds, "The account balance is too low for this amount.");
        }
    }

    /// <summary>Frozen accounts can still receive money; closed ones cannot.</summary>
    public void EnsureCanReceive() => EnsureNotClosed();

    internal void Debit(long amountMinor)
    {
        // System accounts are derived from the ledger only, so they never become a hot row under load.
        if (Kind == AccountKind.Customer)
        {
            BalanceMinor -= amountMinor;
        }
    }

    internal void Credit(long amountMinor)
    {
        if (Kind == AccountKind.Customer)
        {
            BalanceMinor += amountMinor;
        }
    }

    public void Freeze()
    {
        EnsureNotClosed();
        if (Status == AccountStatus.Frozen)
        {
            throw new DomainException(ErrorCodes.AccountFrozen, "The account is already frozen.");
        }

        Status = AccountStatus.Frozen;
    }

    public void Unfreeze()
    {
        EnsureNotClosed();
        if (Status != AccountStatus.Frozen)
        {
            throw new DomainException(ErrorCodes.AccountNotFrozen, "The account is not frozen.");
        }

        Status = AccountStatus.Active;
    }

    public void Close(DateTimeOffset now)
    {
        EnsureNotClosed();
        if (BalanceMinor != 0)
        {
            throw new DomainException(ErrorCodes.AccountHasBalance,
                "Only an account with a zero balance can be closed. Move the money out first.");
        }

        Status = AccountStatus.Closed;
        ClosedAt = now;
    }

    private void EnsureNotClosed()
    {
        if (Status == AccountStatus.Closed)
        {
            throw new DomainException(ErrorCodes.AccountClosed, "The account is closed.");
        }
    }
}
