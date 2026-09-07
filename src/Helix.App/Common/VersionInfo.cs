namespace Helix.App.Common;

internal static class VersionInfo
{
    public static string Display => Format(AppInfo.Current.VersionString);

    public static string Format(string versionString)
    {
        ReadOnlySpan<char> candidate = versionString.AsSpan().Trim();

        int suffix = candidate.IndexOfAny('+', '-');
        if (suffix >= 0)
        {
            candidate = candidate[..suffix];
        }

        if (!Version.TryParse(candidate, out Version? version))
        {
            return versionString;
        }

        return $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }
}
