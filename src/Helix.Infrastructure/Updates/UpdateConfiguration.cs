using System.Net.Http.Headers;
using System.Runtime.InteropServices;

namespace Helix.Infrastructure.Updates;

internal static class UpdateConfiguration
{
    public const string Owner = "HilthonTT";

    public const string Repository = "Helix";

    /// <summary>
    /// The one file per release that carries every archive's signature, keyed by asset
    /// name. Deliberately **not** a `.sig` beside each archive: every build published so
    /// far picks its download with `Name.Contains("-win-x64.")` and no extension filter,
    /// so `Helix-v9.9.9-win-x64.zip.sig` matches that predicate and an existing install
    /// could be handed the signature as its update. This name contains no moniker, so no
    /// released build can select it, whatever order GitHub returns the assets in.
    /// </summary>
    public const string SignatureManifestSuffix = "-signatures.txt";

    /// <summary>
    /// The public half of the release signing key, as a base64 SubjectPublicKeyInfo for an
    /// ECDSA P-256 key — what `openssl ec -in key.pem -pubout -outform DER` produces,
    /// base64'd. Empty until a key is generated and its private half is put in the release
    /// workflow's secrets: while it is empty updates are checked against GitHub's digest
    /// only, exactly as they were before. See docs/release-signing.md.
    /// </summary>
    public const string SigningPublicKey = "";

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
