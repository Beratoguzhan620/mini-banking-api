using MiniBanking.Domain;
using MiniBanking.Domain.Accounts;
using MiniBanking.Domain.Transactions;

namespace MiniBanking.UnitTests;

public class LedgerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Customer = Guid.NewGuid();

    private static Account Funded(long minor, string currency = "TRY")
    {
        var account = Account.Open(Customer, currency, Now);
        if (minor > 0)
        {
            var system = Account.OpenSystem(currency, Now);
            Ledger.Move(BankTransaction.Deposit(Customer, account, minor, null, Now), system, account);
        }

        return account;
    }

    [Fact]
    public void A_transfer_writes_two_entries_that_sum_to_zero()
    {
        var from = Funded(10_000);
        var to = Funded(0);
        var transfer = BankTransaction.Transfer(Customer, from, to, 2_550, "rent", Now);

        var entries = Ledger.Move(transfer, from, to);

        Assert.Equal(0, entries.Sum(e => e.AmountMinor));
        Assert.Equal(-2_550, entries[0].AmountMinor);
        Assert.Equal(7_450, entries[0].BalanceAfterMinor);
        Assert.Equal(2_550, entries[1].BalanceAfterMinor);
        Assert.Equal(7_450, from.BalanceMinor);
        Assert.Equal(2_550, to.BalanceMinor);
    }

    [Fact]
    public void The_system_side_has_no_cached_balance()
    {
        var account = Account.Open(Customer, "USD", Now);
        var system = Account.OpenSystem("USD", Now);

        var entries = Ledger.Move(BankTransaction.Deposit(Customer, account, 500, null, Now), system, account);

        Assert.Null(entries[0].BalanceAfterMinor);
        Assert.Equal(0, system.BalanceMinor);
        Assert.Equal(500, account.BalanceMinor);
    }

    [Fact]
    public void Insufficient_funds_leave_both_balances_untouched()
    {
        var from = Funded(1_000);
        var to = Funded(0);

        var error = Assert.Throws<DomainException>(() =>
            Ledger.Move(BankTransaction.Transfer(Customer, from, to, 1_001, null, Now), from, to));

        Assert.Equal(ErrorCodes.InsufficientFunds, error.Code);
        Assert.Equal(1_000, from.BalanceMinor);
        Assert.Equal(0, to.BalanceMinor);
    }

    [Fact]
    public void A_frozen_account_can_receive_but_not_send()
    {
        var frozen = Funded(1_000);
        var other = Funded(1_000);
        frozen.Freeze();

        Ledger.Move(BankTransaction.Transfer(Customer, other, frozen, 100, null, Now), other, frozen);
        var error = Assert.Throws<DomainException>(() =>
            Ledger.Move(BankTransaction.Transfer(Customer, frozen, other, 100, null, Now), frozen, other));

        Assert.Equal(ErrorCodes.AccountFrozen, error.Code);
        Assert.Equal(1_100, frozen.BalanceMinor);
    }

    [Fact]
    public void A_closed_account_cannot_receive()
    {
        var from = Funded(1_000);
        var closed = Funded(0);
        closed.Close(Now);

        var error = Assert.Throws<DomainException>(() =>
            Ledger.Move(BankTransaction.Transfer(Customer, from, closed, 100, null, Now), from, closed));

        Assert.Equal(ErrorCodes.AccountClosed, error.Code);
    }

    [Fact]
    public void Different_currencies_are_rejected()
    {
        var lira = Funded(1_000, "TRY");
        var euro = Funded(0, "EUR");

        var error = Assert.Throws<DomainException>(() =>
            Ledger.Move(BankTransaction.Transfer(Customer, lira, euro, 100, null, Now), lira, euro));

        Assert.Equal(ErrorCodes.CurrencyMismatch, error.Code);
    }

    [Fact]
    public void An_account_cannot_pay_itself()
    {
        var account = Funded(1_000);

        var error = Assert.Throws<DomainException>(() =>
            Ledger.Move(BankTransaction.Transfer(Customer, account, account, 100, null, Now), account, account));

        Assert.Equal(ErrorCodes.SameAccount, error.Code);
    }
}
