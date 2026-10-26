using MiniBanking.Domain;

namespace MiniBanking.UnitTests;

public class DomainExceptionTests
{
    [Fact]
    public void Keeps_the_machine_readable_code_separate_from_the_message()
    {
        var exception = new DomainException("insufficient_funds", "Balance is too low.");

        Assert.Equal("insufficient_funds", exception.Code);
        Assert.Equal("Balance is too low.", exception.Message);
    }
}
