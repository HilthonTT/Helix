using FluentAssertions;
using Helix.Application.Abstractions.Updates;
using Helix.Infrastructure.Updates;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;

namespace Infrastructure.UnitTests.Updates;

public sealed class GitHubUpdateCheckerTests
{
    private const string CurrentVersion = "2.0.0.3";

    private const string CurrentVersionDisplayed = "2.0.0";

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

    private static GitHubUpdateChecker Checker(
        StubHandler handler,
        string current = CurrentVersion,
        string moniker = "win-x64",
        params string[] fallbacks) =>
        new(new HttpClient(handler),
            NullLogger<GitHubUpdateChecker>.Instance,
            () => current,
            () => [moniker, .. fallbacks]);

    private static string ReleaseJson(string tag, string? url = "https://github.com/HilthonTT/Helix/releases/tag/v2.1.0") =>
        $$"""
        { "tag_name": "{{tag}}", "html_url": "{{url}}", "name": "Helix {{tag}}" }
        """;

    private static string ReleaseWithAssetsJson(string tag) =>
        $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://github.com/HilthonTT/Helix/releases/tag/{{tag}}",
          "assets": [
            { "name": "Helix-{{tag}}-win-arm64.zip", "browser_download_url": "https://example.invalid/arm64" },
            { "name": "Helix-{{tag}}-win-x64.zip", "browser_download_url": "https://example.invalid/x64" },
            { "name": "Helix-{{tag}}-macos.zip", "browser_download_url": "https://example.invalid/macos" }
          ]
        }
        """;

    private static string ReleaseWithX64AssetJson(string tag, string? digest = null) =>
        $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://github.com/HilthonTT/Helix/releases/tag/{{tag}}",
          "assets": [
            {
              "name": "Helix-{{tag}}-win-x64.zip",
              "browser_download_url": "https://example.invalid/x64"{{(digest is null ? "" : $",\n              \"digest\": \"{digest}\"")}}
            }
          ]
        }
        """;

    [Fact]
    public async Task CheckAsync_Should_PickTheAssetBuiltForThisMachine()
    {
        var handler = new StubHandler(() => Json(HttpStatusCode.OK, ReleaseWithAssetsJson("v2.1.0")));

        Result<UpdateCheck> result = await Checker(handler, moniker: "win-x64").CheckAsync();

        result.Value.DownloadUrl.Should().Be("https://example.invalid/x64");
        result.Value.AssetName.Should().Be("Helix-v2.1.0-win-x64.zip");
        result.Value.CanInstall.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAsync_Should_NotHandAnArmMachineTheX64Build()
    {
        var handler = new StubHandler(() => Json(HttpStatusCode.OK, ReleaseWithAssetsJson("v2.1.0")));

        Result<UpdateCheck> result = await Checker(handler, moniker: "win-arm64").CheckAsync();

        result.Value.DownloadUrl.Should().Be("https://example.invalid/arm64");
    }

    [Fact]
    public async Task CheckAsync_Should_PickTheMacBundle_OnMac()
    {
        var handler = new StubHandler(() => Json(HttpStatusCode.OK, ReleaseWithAssetsJson("v2.1.0")));

        Result<UpdateCheck> result = await Checker(handler, moniker: "macos").CheckAsync();

        result.Value.DownloadUrl.Should().Be("https://example.invalid/macos");
    }

    [Fact]
    public async Task CheckAsync_Should_StillReportTheUpdate_WhenTheReleaseHasNoAssetForThisMachine()
    {
        var handler = new StubHandler(() => Json(HttpStatusCode.OK, ReleaseJson("v2.1.0")));

        Result<UpdateCheck> result = await Checker(handler).CheckAsync();

        result.Value.IsUpdateAvailable.Should().BeTrue();
        result.Value.DownloadUrl.Should().BeNull();
        result.Value.CanInstall.Should().BeFalse();
    }

    [Fact]
    public async Task CheckAsync_Should_OfferNothingToInstall_WhenTheArchitectureHasNoBuild()
    {
        var handler = new StubHandler(() => Json(HttpStatusCode.OK, ReleaseWithAssetsJson("v2.1.0")));

        Result<UpdateCheck> result = await Checker(handler, moniker: string.Empty).CheckAsync();

        result.Value.CanInstall.Should().BeFalse();
    }

    [Fact]
    public async Task CheckAsync_Should_ReportAnUpdate_WhenTheReleaseIsNewer()
    {
        var handler = new StubHandler(() => Json(HttpStatusCode.OK, ReleaseJson("v2.1.0")));

        Result<UpdateCheck> result = await Checker(handler).CheckAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.IsUpdateAvailable.Should().BeTrue();
        result.Value.LatestVersion.Should().Be("v2.1.0");
        result.Value.CurrentVersion.Should().Be(CurrentVersionDisplayed);
        result.Value.ReleaseUrl.Should().Be("https://github.com/HilthonTT/Helix/releases/tag/v2.1.0");
    }

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

    [Fact]
    public async Task CheckAsync_Should_CarryTheDigestGitHubPublishesForTheAsset()
    {
        const string digest = "sha256:1507529b5376213a8dd4affbe80ee388b425473676d5ba53e7b11ae67f9907cf";

        var handler = new StubHandler(() => Json(HttpStatusCode.OK, ReleaseWithX64AssetJson("v2.1.0", digest)));

        Result<UpdateCheck> result = await Checker(handler).CheckAsync();

        result.Value.AssetDigest.Should().Be(digest);
    }

    [Fact]
    public async Task CheckAsync_Should_ReportNoDigest_WhenTheReleaseCarriesNone()
    {
        var handler = new StubHandler(() => Json(HttpStatusCode.OK, ReleaseWithX64AssetJson("v2.1.0")));

        Result<UpdateCheck> result = await Checker(handler).CheckAsync();

        result.Value.AssetDigest.Should().BeNull();
        result.Value.CanInstall.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAsync_Should_PreferTheNativeBuild_WhenTheReleaseCarriesBoth()
    {
        var handler = new StubHandler(() => Json(HttpStatusCode.OK, ReleaseWithAssetsJson("v2.1.0")));

        Result<UpdateCheck> result = await Checker(handler, moniker: "win-arm64", fallbacks: "win-x64").CheckAsync();

        result.Value.AssetName.Should().Be("Helix-v2.1.0-win-arm64.zip");
    }

    [Fact]
    public async Task CheckAsync_Should_FallBackToTheEmulatedBuild_WhenTheNativeOneIsNotPublished()
    {
        var handler = new StubHandler(() => Json(HttpStatusCode.OK, ReleaseWithX64AssetJson("v2.1.0")));

        Result<UpdateCheck> result = await Checker(handler, moniker: "win-arm64", fallbacks: "win-x64").CheckAsync();

        result.Value.AssetName.Should().Be("Helix-v2.1.0-win-x64.zip");
        result.Value.CanInstall.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAsync_Should_OfferNothing_WhenOnlyTheOtherArchitectureIsPublished()
    {
        var handler = new StubHandler(() => Json(
            HttpStatusCode.OK,
            """
            {
              "tag_name": "v2.1.0",
              "html_url": "https://github.com/HilthonTT/Helix/releases/tag/v2.1.0",
              "assets": [
                { "name": "Helix-v2.1.0-win-arm64.zip", "browser_download_url": "https://example.invalid/arm64" }
              ]
            }
            """));

        Result<UpdateCheck> result = await Checker(handler, moniker: "win-x64").CheckAsync();

        result.Value.IsUpdateAvailable.Should().BeTrue();
        result.Value.CanInstall.Should().BeFalse();
    }
}
