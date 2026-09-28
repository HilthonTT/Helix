using FluentAssertions;
using Helix.Application.Core.Security;
using Helix.Infrastructure.Cryptography;

namespace Infrastructure.UnitTests.Cryptography;

public sealed class RecoveryKeyGeneratorTests
{
    private readonly RecoveryKeyGenerator _generator = new();

    [Fact]
    public void Generate_Should_ProduceAKeyThatNormalizesToItself()
    {
        string key = _generator.Generate();

        key.Should().MatchRegex("^[0-9A-Z]{5}(-[0-9A-Z]{5}){4}$");
        RecoveryKeyFormat.Normalize(key).Should().Be(key);
    }

    [Fact]
    public void Generate_Should_NotRepeat()
    {
        string[] keys = Enumerable.Range(0, 100).Select(_ => _generator.Generate()).ToArray();

        keys.Should().OnlyHaveUniqueItems();
    }
}
