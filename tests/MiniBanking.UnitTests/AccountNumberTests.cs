using MiniBanking.Domain.Accounts;

namespace MiniBanking.UnitTests;

public class AccountNumberTests
{
    [Fact]
    public void Generated_numbers_pass_their_own_check_digit()
    {
        for (var i = 0; i < 1_000; i++)
        {
            Assert.True(AccountNumbers.IsValid(AccountNumbers.Generate()));
        }
    }

    [Fact]
    public void A_single_mistyped_digit_is_caught()
    {
        var number = AccountNumbers.Generate();
        var position = 5;
        var wrongDigit = (char)('0' + ((number[position] - '0' + 1) % 10));
        var mistyped = number[..position] + wrongDigit + number[(position + 1)..];

        Assert.False(AccountNumbers.IsValid(mistyped));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("MB123")]
    [InlineData("XX0000000000000000")]
    [InlineData("MB000000000000000A")]
    public void Malformed_numbers_are_invalid(string? number)
    {
        Assert.False(AccountNumbers.IsValid(number));
    }

    [Fact]
    public void Normalize_removes_spaces_and_upper_cases()
    {
        Assert.Equal("MB1234567890123456", AccountNumbers.Normalize(" mb 1234 5678 9012 3456 "));
    }
}
