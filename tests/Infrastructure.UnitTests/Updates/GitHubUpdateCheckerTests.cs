using FluentAssertions;
using Helix.Application.Abstractions.Updates;
using Helix.Infrastructure.Updates;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;

namespace Infrastructure.UnitTests.Updates;

/// <summary>
/// Covers how the checker reads GitHub's answers, including the ones that are not a
/// release: offline, rate-limited, nothing published yet, and a tag that is not a version.
/// </summary>
public sealed class GitHubUpdateCheckerTests
{
    private const string CurrentVersion = "2.0.0.0";

    /// <summary>Answers every request with a canned response, or throws.</summary>
    private sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;

            return Task.FromResult(respond());
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static GitHubUpdateChecker Checker(StubHandler handler, string current = CurrentVersion) =>
        new(new HttpClient(handler), NullLogger<GitHubUpdateChecker>.Instance, () => current);

    private static string ReleaseJson(string tag, string? url = "https://github.com/HilthonTT/Helix/releases/tag/v2.1.0") =>
        $$"""
        { "tag_name": "{{tag}}", "html_url": "{{url}}", "name": "Helix {{tag}}" }
        """;

    [Fact]
    public async Task CheckAsync_Should_ReportAnUpdate_WhenTheReleaseIsNewer()
    {
        var handler = new StubHandler(() => Json(HttpStatusCode.OK, ReleaseJson("v2.1.0")));

        Result<UpdateCheck> result = await Checker(handler).CheckAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.IsUpdateAvailable.Should().BeTrue();
        result.Value.LatestVersion.Should().Be("v2.1.0");
        result.Value.CurrentVersion.Should().Be(CurrentVersion);
        result.Value.ReleaseUrl.Should().Be("https://github.com/HilthonTT/Helix/releases/tag/v2.1.0");
    }

    /// <summary>
    /// The build this repository currently produces, against the tag it was released
    /// under. It must not announce an update to itself.
    /// </summary>
    [Fact]
    public async Task CheckAsync_Should_ReportNoUpdate_WhenTheReleaseIsTheRunningBuild()
    {
        var handler = new StubHandler(() => Json(HttpStatusCode.OK, ReleaseJson("v2.0.0")));

        Result<UpdateCheck> result = await Checker(handler).CheckAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.IsUpdateAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task CheckAsync_Should_SendTheHeadersGitHubRequires()
    {
        var handler = new StubHandler(() => Json(HttpStatusCode.OK, ReleaseJson("v2.0.0")));

        var client = UpdateConfiguration.CreateHttpClient();

        // GitHub answers 403 to a request with no User-Agent, so this is not optional.
        client.DefaultRequestHeaders.UserAgent.Should().NotBeEmpty();
        client.DefaultRequestHeaders.Accept.Should().Contain(h => h.MediaType == "application/vnd.github+json");

        await Checker(handler).CheckAsync();

        handler.LastRequest!.RequestUri!.ToString().Should().Be(UpdateConfiguration.LatestReleaseUrl);
    }

    [Fact]
    public async Task CheckAsync_Should_ReportUnreachable_WhenTheRequestFails()
    {
        var handler = new StubHandler(() => throw new HttpRequestException("no network"));

        Result<UpdateCheck> result = await Checker(handler).CheckAsync();

        result.Error.Should().Be(UpdateErrors.Unreachable);
    }

    [Fact]
    public async Task CheckAsync_Should_ReportNoReleases_WhenNothingIsPublished()
    {
        var handler = new StubHandler(() => Json(HttpStatusCode.NotFound, "{}"));

        Result<UpdateCheck> result = await Checker(handler).CheckAsync();

        result.Error.Should().Be(UpdateErrors.NoReleases);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task CheckAsync_Should_ReportRateLimiting_Distinctly(HttpStatusCode status)
    {
        // "Try again later" is actionable; a bare 403 reads like something is broken.
        var handler = new StubHandler(() => Json(status, "{}"));

        Result<UpdateCheck> result = await Checker(handler).CheckAsync();

        result.Error.Should().Be(UpdateErrors.RateLimited);
    }

    [Fact]
    public async Task CheckAsync_Should_ReportTheStatus_WhenGitHubAnswersUnexpectedly()
    {
        var handler = new StubHandler(() => Json(HttpStatusCode.InternalServerError, "{}"));

        Result<UpdateCheck> result = await Checker(handler).CheckAsync();

        result.Error.Should().Be(UpdateErrors.UnexpectedResponse(500));
    }

    [Fact]
    public async Task CheckAsync_Should_Refuse_WhenTheLatestReleaseIsNotTaggedWithAVersion()
    {
        var handler = new StubHandler(() => Json(HttpStatusCode.OK, ReleaseJson("nightly")));

        Result<UpdateCheck> result = await Checker(handler).CheckAsync();

        result.Error.Should().Be(UpdateErrors.UnreadableRelease);
    }

    [Fact]
    public async Task CheckAsync_Should_Refuse_WhenTheBodyIsNotARelease()
    {
        var handler = new StubHandler(() => Json(HttpStatusCode.OK, "not json at all"));

        Result<UpdateCheck> result = await Checker(handler).CheckAsync();

        result.Error.Should().Be(UpdateErrors.UnreadableRelease);
    }

    [Fact]
    public async Task CheckAsync_Should_FallBackToTheReleasesPage_WhenTheReleaseHasNoUrl()
    {
        var handler = new StubHandler(() => Json(
            HttpStatusCode.OK,
            """{ "tag_name": "v2.1.0" }"""));

        Result<UpdateCheck> result = await Checker(handler).CheckAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.ReleaseUrl.Should().Be(UpdateConfiguration.ReleasesPageUrl);
    }
}
