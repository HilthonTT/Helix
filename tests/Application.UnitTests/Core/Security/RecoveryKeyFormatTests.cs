using FluentAssertions;
using Helix.Application.Core.Security;

namespace Application.UnitTests.Core.Security;

public sealed class RecoveryKeyFormatTests
{
    [Theory]
    [InlineData("ABCDE-FGHJK-MNPQR-STVWX-YZ012", "ABCDE-FGHJK-MNPQR-STVWX-YZ012")]
    [InlineData("abcdefghjkmnpqrstvwxyz012", "ABCDE-FGHJK-MNPQR-STVWX-YZ012")]
    [InlineData("  abcde fghjk\tmnpqr-stvwx yz012 ", "ABCDE-FGHJK-MNPQR-STVWX-YZ012")]
    [InlineData("OOOOO-IIIII-LLLLL-00000-11111", "00000-11111-11111-00000-11111")]
    public void Normalize_Should_ReturnTheCanonicalForm(string input, string expected)
    {
        RecoveryKeyFormat.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABCDE-FGHJK-MNPQR-STVWX-YZ01")]
    [InlineData("ABCDE-FGHJK-MNPQR-STVWX-YZ0123")]
    [InlineData("ABCDE-FGHJK-MNPQR-STVWX-YZ01U")]
    [InlineData("ABCDE-FGHJK-MNPQR-STVWX-YZ01!")]
    public void Normalize_Should_RefuseAnythingThatIsNotAKey(string? input)
    {
        RecoveryKeyFormat.Normalize(input).Should().BeNull();
    }
}
