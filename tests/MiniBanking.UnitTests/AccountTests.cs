using MiniBanking.Domain;
using MiniBanking.Domain.Accounts;

namespace MiniBanking.UnitTests;

public class AccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_account_is_active_with_a_valid_number_and_no_money()
    {
        var account = Account.Open(Guid.NewGuid(), " try ", Now);

        Assert.Equal(AccountStatus.Active, account.Status);
        Assert.Equal("TRY", account.Currency);
        Assert.Equal(0, account.BalanceMinor);
        Assert.True(AccountNumbers.IsValid(account.Number));
    }

    [Fact]
    public void Unsupported_currency_is_a_domain_error()
    {
        var error = Assert.Throws<DomainException>(() => Account.Open(Guid.NewGuid(), "GBP", Now));

        Assert.Equal(ErrorCodes.UnsupportedCurrency, error.Code);
    }

    [Fact]
    public void Freezing_twice_and_unfreezing_an_active_account_are_rejected()
    {
        var account = Account.Open(Guid.NewGuid(), "TRY", Now);

        Assert.Equal(ErrorCodes.AccountNotFrozen, Assert.Throws<DomainException>(account.Unfreeze).Code);
        account.Freeze();
        Assert.Equal(ErrorCodes.AccountFrozen, Assert.Throws<DomainException>(account.Freeze).Code);
    }

    [Fact]
    public void A_closed_account_cannot_change_any_more()
    {
        var account = Account.Open(Guid.NewGuid(), "TRY", Now);
        account.Close(Now);

        Assert.Equal(AccountStatus.Closed, account.Status);
        Assert.Equal(Now, account.ClosedAt);
        Assert.Equal(ErrorCodes.AccountClosed, Assert.Throws<DomainException>(account.Freeze).Code);
        Assert.Equal(ErrorCodes.AccountClosed, Assert.Throws<DomainException>(() => account.Close(Now)).Code);
    }
}
