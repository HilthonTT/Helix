using System.Net.Http.Headers;
using System.Runtime.InteropServices;

namespace Helix.Infrastructure.Updates;

internal static class UpdateConfiguration
{
    public const string Owner = "HilthonTT";

    public const string Repository = "Helix";

    public static string LatestReleaseUrl => $"https://api.github.com/repos/{Owner}/{Repository}/releases/latest";

    public static string ReleasesPageUrl => $"https://github.com/{Owner}/{Repository}/releases";

    public static IReadOnlyList<string> AssetMonikers
    {
        get
        {
#if MACCATALYST
            return ["macos"];
#else
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => ["win-arm64", "win-x64"],
                Architecture.X64 => ["win-x64"],
                _ => [],
            };
#endif
        }
    }

    public static HttpClient CreateDownloadHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30),
        };

        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(Repository, "1.0"));

        return client;
    }

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
