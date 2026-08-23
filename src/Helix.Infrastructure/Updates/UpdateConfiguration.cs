using System.Net.Http.Headers;
using System.Runtime.InteropServices;

namespace Helix.Infrastructure.Updates;

internal static class UpdateConfiguration
{
    /// <summary>
    /// The repository releases are published from. Matches the tag pattern the release
    /// workflow triggers on (<c>v*</c>) and the link in the sidebar footer.
    /// </summary>
    public const string Owner = "HilthonTT";

    public const string Repository = "Helix";

    /// <summary>
    /// The newest published, non-draft, non-prerelease release.
    /// </summary>
    /// <remarks>
    /// <c>/releases/latest</c> rather than the tag list on purpose. Tags exist for things
    /// that were never released, and pre-releases are not something an unattended NAS tool
    /// should be nudging people onto. It answers 404 when there is no stable release,
    /// which is reported as such rather than as a failure.
    /// </remarks>
    public static string LatestReleaseUrl => $"https://api.github.com/repos/{Owner}/{Repository}/releases/latest";

    /// <summary>Where to send the user when there is something newer.</summary>
    public static string ReleasesPageUrl => $"https://github.com/{Owner}/{Repository}/releases";

    /// <summary>
    /// The fragment that identifies this machine's build in a release asset's name.
    /// </summary>
    /// <remarks>
    /// Matches how the release workflow names its archives — <c>Helix-v2.1.0-win-x64.zip</c>,
    /// <c>-win-arm64</c>, <c>-macos</c>. An architecture with no published build answers
    /// empty rather than guessing, which reads downstream as "no download for this
    /// computer" and leaves the release page as the route.
    /// </remarks>
    public static string AssetMoniker
    {
        get
        {
#if MACCATALYST
            return "macos";
#else
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "win-arm64",
                Architecture.X64 => "win-x64",
                _ => string.Empty,
            };
#endif
        }
    }

    /// <summary>
    /// A client for pulling down a release archive, as opposed to reading the API.
    /// </summary>
    /// <remarks>
    /// Its own client because the two want opposite things from a timeout: the API call
    /// hangs off a button and must give up quickly, while this moves a couple of hundred
    /// megabytes over whatever connection the user has. The timeout here bounds the whole
    /// transfer, so it is generous rather than short; a user who changes their mind
    /// cancels, and cancelling is instant.
    /// </remarks>
    public static HttpClient CreateDownloadHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30),
        };

        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(Repository, "1.0"));

        return client;
    }

    /// <summary>
    /// A client configured the way the GitHub API expects.
    /// </summary>
    /// <remarks>
    /// The User-Agent is not optional — GitHub rejects requests without one. The timeout
    /// is short because this hangs off a button the user is waiting on, and the whole
    /// feature is optional.
    /// </remarks>
    public static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };

        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(Repository, "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        return client;
    }
}
