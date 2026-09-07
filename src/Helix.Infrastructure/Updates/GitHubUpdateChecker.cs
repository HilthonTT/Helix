using Helix.Application.Abstractions.Updates;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Helix.Infrastructure.Updates;

internal sealed class GitHubUpdateChecker : IUpdateChecker
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubUpdateChecker> _logger;
    private readonly Func<string> _currentVersion;
    private readonly Func<IReadOnlyList<string>> _assetMonikers;

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
            _logger.LogWarning(ex, "The update check could not reach GitHub.");

            return Result.Failure<UpdateCheck>(UpdateErrors.Unreachable);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Result.Failure<UpdateCheck>(UpdateErrors.NoReleases);
            }

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

            string url = string.IsNullOrWhiteSpace(release.HtmlUrl)
                ? UpdateConfiguration.ReleasesPageUrl
                : release.HtmlUrl;

            GitHubAsset? asset = SelectAsset(release);

            if (isNewer && asset is null)
            {
                _logger.LogInformation(
                    "Release {Tag} carries no asset for {Monikers}; the release page is the only route.",
                    release.TagName,
                    string.Join(", ", _assetMonikers()));
            }

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

        [JsonPropertyName("digest")]
        public string? Digest { get; init; }
    }
}
