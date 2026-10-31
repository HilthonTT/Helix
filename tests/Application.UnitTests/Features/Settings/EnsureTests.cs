using FluentAssertions;

namespace Application.UnitTests.Features.Settings;

public sealed class EnsureTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MustBePositive_Should_Throw_ForZeroOrLess(int value)
    {
        Action act = () => Ensure.MustBePositive(value);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MustBePositive_Should_Accept_One()
    {
        Action act = () => Ensure.MustBePositive(1);

        act.Should().NotThrow();
    }
}
