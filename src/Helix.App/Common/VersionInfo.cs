namespace Helix.App.Common;

/// <summary>
/// The running version, in the three-part form the releases are tagged with.
/// </summary>
/// <remarks>
/// Shared rather than private to the sidebar, because the sign-in pages show the same
/// number and two formatters would eventually disagree about it. Static because its
/// callers — <c>AppShell</c> and the viewmodels behind the sign-in pages — are built by
/// MAUI rather than the container, and there is no state here to inject anyway.
/// </remarks>
internal static class VersionInfo
{
    /// <summary>The running version as "2.2.1", ready to put in front of the user.</summary>
    public static string Display => Format(AppInfo.Current.VersionString);

    /// <summary>
    /// Reduces what the build reports to the three components a release is tagged with.
    /// </summary>
    /// <remarks>
    /// Always three, so what is shown matches the tag on the releases page exactly and
    /// can be compared against it at a glance. Trimming to major.minor showed 2.2.1 as
    /// "2.2" and 2.0.1 as "2.0" — in the second case still naming the version the user
    /// had before updating.
    /// </remarks>
    public static string Format(string versionString)
    {
        ReadOnlySpan<char> candidate = versionString.AsSpan().Trim();

        // The build carries two version strings — a four-part file version (2.2.1.0) and
        // an informational one with the commit appended (2.2.1+23d2862...) — and which of
        // them AppInfo hands back depends on how the app was packaged. Version.TryParse
        // rejects the second outright, which would drop the raw string, commit hash and
        // all, into the UI. Trimmed here so either shape reads the same.
        int suffix = candidate.IndexOfAny('+', '-');
        if (suffix >= 0)
        {
            candidate = candidate[..suffix];
        }

        if (!Version.TryParse(candidate, out Version? version))
        {
            return versionString;
        }

        // Build is -1 when the string had only two components; a release is always
        // tagged with three, so it reads as the zero it stands for.
        return $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }
}
