using FluentAssertions;
using Helix.Infrastructure.Updates;

namespace Infrastructure.UnitTests.Updates;

/// <summary>
/// Covers the version comparison, which is where a "check for updates" feature quietly
/// goes wrong: it either nags about a version already installed, or never reports one.
/// </summary>
public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("v2.0.0", "2.0.0.0")]
    [InlineData("V2.0.0", "2.0.0.0")]   // Some tags are capitalised
    [InlineData("2.1", "2.1.0.0")]      // ApplicationDisplayVersion is two-part
    [InlineData("2.1.3.4", "2.1.3.4")]
    [InlineData("  v1.0.0  ", "1.0.0.0")]
    [InlineData("v2.1.0-beta.1", "2.1.0.0")] // Pre-release suffix is dropped
    [InlineData("v2.1.0+build7", "2.1.0.0")] // Build metadata likewise
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

    /// <summary>
    /// The trap this normalization exists for. Helix declares its version as "2.0" and
    /// tags its releases "v2.0.0"; <see cref="Version"/> treats a missing component as -1,
    /// so without widening, 2.0 sorts below 2.0.0 and the app would announce an update to
    /// the exact build already running.
    /// </summary>
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

    /// <summary>
    /// What the user is shown must be the number on the releases page, so the build
    /// counter Windows appends is dropped and a missing component reads as the zero it
    /// stands for.
    /// </summary>
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

    /// <summary>
    /// A version that cannot be read is handed back rather than blanked: showing nothing
    /// where a version belongs looks like a bug, and the raw string is at least a clue.
    /// </summary>
    [Theory]
    [InlineData("nightly", "nightly")]
    [InlineData(null, "")]
    public void ToDisplayString_Should_KeepWhatItCannotRead(string? value, string expected)
    {
        ReleaseVersion.ToDisplayString(value).Should().Be(expected);
    }

    /// <summary>
    /// An update the user cannot verify is worse than no update, so an unreadable version
    /// on either side means "no update", never "maybe".
    /// </summary>
    [Theory]
    [InlineData("nightly", "2.0.0.0")]
    [InlineData("v2.1.0", "unknown")]
    [InlineData(null, null)]
    public void IsNewerThan_Should_ReportNothing_WhenEitherSideCannotBeRead(string? tag, string? current)
    {
        ReleaseVersion.IsNewerThan(tag, current).Should().BeFalse();
    }
}
