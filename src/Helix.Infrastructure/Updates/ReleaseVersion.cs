using System.Diagnostics.CodeAnalysis;

namespace Helix.Infrastructure.Updates;

/// <summary>
/// Turns the version strings on either side of the comparison into something that can
/// actually be compared.
/// </summary>
/// <remarks>
/// Separate from the checker, and internal rather than private, because this is where the
/// whole feature can quietly go wrong and it is worth testing on its own.
///
/// The trap is <see cref="Version"/>'s treatment of absent components as -1, not 0. Helix
/// declares <c>ApplicationDisplayVersion</c> as <c>2.0</c> and tags its releases
/// <c>v2.0.0</c>, so an unnormalized comparison makes the running build "older" than the
/// release it was built from and nags the user to install what they already have. Both
/// sides are widened to four components before anything is compared.
/// </remarks>
internal static class ReleaseVersion
{
    /// <summary>
    /// Parses a release tag or an application version into a comparable four-part number.
    /// </summary>
    /// <remarks>
    /// Accepts the shapes that actually turn up: a <c>v</c> prefix as GitHub tags carry,
    /// and a pre-release or build suffix (<c>v2.1.0-beta.1</c>, <c>2.1.0+build7</c>),
    /// which is dropped — a suffix distinguishes builds of one version, and treating it
    /// as part of the number is not something <see cref="Version"/> can do anyway.
    /// </remarks>
    public static bool TryParse(string? value, [NotNullWhen(true)] out Version? version)
    {
        version = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        ReadOnlySpan<char> candidate = value.AsSpan().Trim();

        if (candidate.Length > 0 && (candidate[0] == 'v' || candidate[0] == 'V'))
        {
            candidate = candidate[1..];
        }

        int suffix = candidate.IndexOfAny('-', '+');
        if (suffix >= 0)
        {
            candidate = candidate[..suffix];
        }

        if (!Version.TryParse(candidate, out Version? parsed))
        {
            return false;
        }

        version = Normalize(parsed);

        return true;
    }

    /// <summary>
    /// Widens a version to four components, so that 2.0, 2.0.0 and 2.0.0.0 are one number.
    /// </summary>
    private static Version Normalize(Version version) => new(
        version.Major,
        version.Minor,
        Math.Max(0, version.Build),
        Math.Max(0, version.Revision));

    /// <summary>
    /// Whether <paramref name="latestTag"/> names a version newer than
    /// <paramref name="currentVersion"/>. False whenever either side cannot be read: an
    /// update prompt the user cannot verify is worse than no prompt.
    /// </summary>
    public static bool IsNewerThan(string? latestTag, string? currentVersion)
    {
        return TryParse(latestTag, out Version? latest) &&
               TryParse(currentVersion, out Version? current) &&
               latest > current;
    }
}
