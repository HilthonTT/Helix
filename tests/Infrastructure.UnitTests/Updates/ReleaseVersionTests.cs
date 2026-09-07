using FluentAssertions;
using Helix.Infrastructure.Updates;

namespace Infrastructure.UnitTests.Updates;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("v2.0.0", "2.0.0.0")]
    [InlineData("V2.0.0", "2.0.0.0")]
    [InlineData("2.1", "2.1.0.0")]
    [InlineData("2.1.3.4", "2.1.3.4")]
    [InlineData("  v1.0.0  ", "1.0.0.0")]
    [InlineData("v2.1.0-beta.1", "2.1.0.0")]
    [InlineData("v2.1.0+build7", "2.1.0.0")]
    public void TryParse_Should_NormalizeToFourComponents(string tag, string expected)
    {
        ReleaseVersion.TryParse(tag, out Version? version).Should().BeTrue();

        version!.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("v")]
    [InlineData("latest")]
    [InlineData("release-2024")]
    public void TryParse_Should_Fail_OnAnythingThatIsNotAVersion(string? tag)
    {
        ReleaseVersion.TryParse(tag, out Version? version).Should().BeFalse();

        version.Should().BeNull();
    }

    [Theory]
    [InlineData("v2.0.0", "2.0")]
    [InlineData("v2.0", "2.0.0.0")]
    [InlineData("v2.0.0", "2.0.0.0")]
    public void IsNewerThan_Should_TreatTheSameVersionWrittenDifferentlyAsEqual(string tag, string current)
    {
        ReleaseVersion.IsNewerThan(tag, current).Should().BeFalse();
    }

    [Theory]
    [InlineData("v2.1.0", "2.0.0.0")]
    [InlineData("v2.0.1", "2.0.0.0")]
    [InlineData("v3.0.0", "2.9.9.9")]
    [InlineData("v2.0.0.1", "2.0")]
    public void IsNewerThan_Should_ReportAnUpdate_WhenTheReleaseIsAhead(string tag, string current)
    {
        ReleaseVersion.IsNewerThan(tag, current).Should().BeTrue();
    }

    [Theory]
    [InlineData("v1.0.0", "2.0.0.0")]
    [InlineData("v2.0.0", "2.1.0.0")]
    public void IsNewerThan_Should_ReportNothing_WhenTheReleaseIsBehind(string tag, string current)
    {
        ReleaseVersion.IsNewerThan(tag, current).Should().BeFalse();
    }

    [Theory]
    [InlineData("2.1.0.3", "2.1.0")]
    [InlineData("2.1.0.0", "2.1.0")]
    [InlineData("2.1", "2.1.0")]
    [InlineData("v2.1.0", "2.1.0")]
    [InlineData("2.1.0+9e9038a", "2.1.0")]
    public void ToDisplayString_Should_ReduceToTheTaggedForm(string value, string expected)
    {
        ReleaseVersion.ToDisplayString(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("nightly", "nightly")]
    [InlineData(null, "")]
    public void ToDisplayString_Should_KeepWhatItCannotRead(string? value, string expected)
    {
        ReleaseVersion.ToDisplayString(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("nightly", "2.0.0.0")]
    [InlineData("v2.1.0", "unknown")]
    [InlineData(null, null)]
    public void IsNewerThan_Should_ReportNothing_WhenEitherSideCannotBeRead(string? tag, string? current)
    {
        ReleaseVersion.IsNewerThan(tag, current).Should().BeFalse();
    }
}
