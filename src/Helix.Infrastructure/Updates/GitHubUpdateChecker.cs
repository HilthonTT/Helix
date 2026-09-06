using Helix.Application.Abstractions.Updates;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Helix.Infrastructure.Updates;

/// <summary>
/// Compares the running build against the latest release on GitHub.
/// </summary>
/// <remarks>
/// Read-only and unauthenticated: it fetches one small JSON document and reports what it
/// says. Nothing is downloaded, nothing is installed, and no token is involved — the
/// releases endpoint is public, and asking the user for a credential to read a public page
/// would be a poor trade for a convenience feature.
/// </remarks>
internal sealed class GitHubUpdateChecker : IUpdateChecker
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubUpdateChecker> _logger;
    private readonly Func<string> _currentVersion;
    private readonly Func<IReadOnlyList<string>> _assetMonikers;

    /// <param name="currentVersion">
    /// Injected rather than read inline so the comparison can be tested without a MAUI
    /// host, which is where <c>AppInfo</c> comes from.
    /// </param>
    /// <param name="assetMonikers">
    /// The fragments a release asset's name may contain to be one this machine can run —
    /// <c>win-x64</c>, <c>win-arm64</c> or <c>macos</c>, matching what the release
    /// workflow names its archives — best first. Injected for the same reason the version
    /// is: so the selection can be tested without being on the machine it selects for.
    /// </param>
    public GitHubUpdateChecker(
        HttpClient httpClient,
        ILogger<GitHubUpdateChecker> logger,
        Func<string> currentVersion,
        Func<IReadOnlyList<string>> assetMonikers)
    {
        _httpClient = httpClient;
        _logger = logger;
        _currentVersion = currentVersion;
        _assetMonikers = assetMonikers;
    }

    public async Task<Result<UpdateCheck>> CheckAsync(CancellationToken cancellationToken = default)
    {
        string current = _currentVersion();

        HttpResponseMessage response;

        try
        {
            response = await _httpClient.GetAsync(UpdateConfiguration.LatestReleaseUrl, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Offline, DNS down, or slower than the timeout. All the same to the user, and
            // none of them are worth an exception escaping a button press.
            _logger.LogWarning(ex, "The update check could not reach GitHub.");

            return Result.Failure<UpdateCheck>(UpdateErrors.Unreachable);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Result.Failure<UpdateCheck>(UpdateErrors.NoReleases);
            }

            // GitHub reports an exhausted quota as 403 with the remaining count at zero,
            // which is a wait-and-retry rather than anything the user did wrong.
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                return Result.Failure<UpdateCheck>(UpdateErrors.RateLimited);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "The update check got an unexpected status {Status} from GitHub.",
                    (int)response.StatusCode);

                return Result.Failure<UpdateCheck>(UpdateErrors.UnexpectedResponse((int)response.StatusCode));
            }

            GitHubRelease? release;

            try
            {
                await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);

                release = await JsonSerializer.DeserializeAsync<GitHubRelease>(body, JsonOptions, cancellationToken);
            }
            catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "The release GitHub returned could not be read.");

                return Result.Failure<UpdateCheck>(UpdateErrors.UnreadableRelease);
            }

            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return Result.Failure<UpdateCheck>(UpdateErrors.UnreadableRelease);
            }

            // A tag that is not a version number cannot be compared, and guessing would
            // mean telling the user to install something on no evidence.
            if (!ReleaseVersion.TryParse(release.TagName, out _))
            {
                _logger.LogWarning("The latest release is tagged {Tag}, which is not a version.", release.TagName);

                return Result.Failure<UpdateCheck>(UpdateErrors.UnreadableRelease);
            }

            bool isNewer = ReleaseVersion.IsNewerThan(release.TagName, current);

            _logger.LogInformation(
                "Update check: running {Current}, latest release {Latest}, update available: {Available}.",
                current,
                release.TagName,
                isNewer);

            // Falls back to the releases page when a release carries no html_url, so the
            // "open" action always has somewhere to go.
            string url = string.IsNullOrWhiteSpace(release.HtmlUrl)
                ? UpdateConfiguration.ReleasesPageUrl
                : release.HtmlUrl;

            GitHubAsset? asset = SelectAsset(release);

            if (isNewer && asset is null)
            {
                // Worth a line: the difference between "no update" and "an update nobody
                // can install from in here" is invisible from the dialog otherwise.
                _logger.LogInformation(
                    "Release {Tag} carries no asset for {Monikers}; the release page is the only route.",
                    release.TagName,
                    string.Join(", ", _assetMonikers()));
            }

            // Compared as reported, shown three-part: the comparison wants every
            // component Windows gives it, the user wants the number on the releases page.
            return new UpdateCheck(
                isNewer,
                ReleaseVersion.ToDisplayString(current),
                release.TagName,
                url,
                asset?.BrowserDownloadUrl,
                asset?.Name,
                asset?.Digest);
        }
    }

    /// <summary>
    /// The best asset this machine can run, or null if the release has none.
    /// </summary>
    /// <remarks>
    /// Matched on the file name rather than on any field GitHub provides, because there
    /// is no such field — the release workflow encodes the target in the archive's name
    /// (<c>Helix-v2.1.0-win-x64.zip</c>) and that is the only thing to go on.
    ///
    /// <c>win-x64</c> is a substring of nothing else, but the arm64 name would match a
    /// naive contains-check for x64 in neither direction — so the comparison is exact
    /// enough to be safe: an x64 machine must never be handed the arm64 build, which
    /// would install cleanly and then refuse to start.
    ///
    /// The candidates are walked in the order they are given rather than matched all at
    /// once, because that order is the whole of what makes a fallback safe: an Arm64
    /// machine takes the Arm64 build where there is one, and the emulated x64 build only
    /// where there is not.
    /// </remarks>
    private GitHubAsset? SelectAsset(GitHubRelease release)
    {
        if (release.Assets is null || release.Assets.Count == 0)
        {
            return null;
        }

        IReadOnlyList<string> monikers = _assetMonikers();

        for (int index = 0; index < monikers.Count; index++)
        {
            string moniker = monikers[index];

            if (string.IsNullOrWhiteSpace(moniker))
            {
                continue;
            }

            // The archive itself, not a sidecar named after it: a checksum or signature
            // file published as Helix-v2.3.0-win-x64.zip.sha256 carries the same moniker
            // and would be staged as the build, then fail to unpack on every install.
            GitHubAsset? match = release.Assets.FirstOrDefault(asset =>
                !string.IsNullOrWhiteSpace(asset.Name) &&
                !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl) &&
                asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                asset.Name.Contains($"-{moniker}.", StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                continue;
            }

            if (index > 0)
            {
                // Worth saying out loud: the machine is about to be handed a build that
                // is not the one for it, and the reason is that the release carries no
                // such build rather than anything about the machine.
                _logger.LogInformation(
                    "Release {Tag} has no {Preferred} asset; falling back to {Moniker}.",
                    release.TagName,
                    monikers[0],
                    moniker);
            }

            return match;
        }

        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Only the fields of the release payload that are used.</summary>
    private sealed record GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; init; }
    }

    private sealed record GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; init; }

        /// <summary>GitHub's own digest of the asset, as <c>sha256:&lt;hex&gt;</c>.</summary>
        /// <remarks>
        /// Absent from releases published before GitHub began returning the field, which
        /// is why everything downstream treats it as optional rather than as something it
        /// can insist on.
        /// </remarks>
        [JsonPropertyName("digest")]
        public string? Digest { get; init; }
    }
}
