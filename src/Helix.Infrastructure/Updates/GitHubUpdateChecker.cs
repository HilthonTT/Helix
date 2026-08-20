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

    /// <param name="currentVersion">
    /// Injected rather than read inline so the comparison can be tested without a MAUI
    /// host, which is where <c>AppInfo</c> comes from.
    /// </param>
    public GitHubUpdateChecker(
        HttpClient httpClient,
        ILogger<GitHubUpdateChecker> logger,
        Func<string> currentVersion)
    {
        _httpClient = httpClient;
        _logger = logger;
        _currentVersion = currentVersion;
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

            // Compared as reported, shown three-part: the comparison wants every
            // component Windows gives it, the user wants the number on the releases page.
            return new UpdateCheck(
                isNewer,
                ReleaseVersion.ToDisplayString(current),
                release.TagName,
                url);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Only the three fields of the release payload that are used.</summary>
    private sealed record GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }
    }
}
