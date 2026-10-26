using MiniBanking.Domain;

namespace MiniBanking.UnitTests;

public class AmountsTests
{
    [Theory]
    [InlineData("0.01", 1)]
    [InlineData("12.5", 1_250)]
    [InlineData("10000000", 1_000_000_000)]
    public void Valid_amounts_become_minor_units(string amount, long expected)
    {
        Assert.True(Amounts.TryToMinor(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), out var minor));
        Assert.Equal(expected, minor);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.001")]
    [InlineData("10000000.01")]
    public void Zero_negative_too_precise_and_too_large_amounts_are_rejected(string amount)
    {
        Assert.False(Amounts.TryToMinor(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), out _));
    }

    [Fact]
    public void Minor_units_convert_back_without_rounding()
    {
        Assert.Equal(1234.56m, Amounts.ToMajor(123_456));
    }
}
