using System.Diagnostics.CodeAnalysis;

namespace Helix.Infrastructure.Updates;

internal static class ReleaseVersion
{
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

    private static Version Normalize(Version version) => new(
        version.Major,
        version.Minor,
        Math.Max(0, version.Build),
        Math.Max(0, version.Revision));

    public static string ToDisplayString(string? value)
    {
        return TryParse(value, out Version? version)
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : value ?? string.Empty;
    }

    public static bool IsNewerThan(string? latestTag, string? currentVersion)
    {
        return TryParse(latestTag, out Version? latest) &&
               TryParse(currentVersion, out Version? current) &&
               latest > current;
    }
}
