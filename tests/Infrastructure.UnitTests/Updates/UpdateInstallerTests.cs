using FluentAssertions;
using Helix.Application.Abstractions.Updates;
using Helix.Infrastructure.Updates;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.UnitTests.Updates;

public sealed class UpdateInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"helix-update-tests-{Guid.NewGuid():N}");

    private string InstallDirectory => Path.Combine(_root, "install");

    private string StagingRoot => Path.Combine(_root, "staging");

    private string LogDirectory => Path.Combine(_root, "logs");

    public UpdateInstallerTests()
    {
        Directory.CreateDirectory(InstallDirectory);
        Directory.CreateDirectory(StagingRoot);

        File.WriteAllText(Path.Combine(InstallDirectory, "Helix.App.exe"), "binary");
    }

    [Fact]
    public async Task StageAsync_Should_Refuse_AFolderThatDoesNotHoldHelix()
    {
        File.Delete(Path.Combine(InstallDirectory, "Helix.App.exe"));

        UpdateInstaller installer = Installer(() => Zip(("Helix.App.exe", "binary")));

        Result<string> result = await installer.StageAsync(Update());

        result.Error.Should().Be(UpdateErrors.UnsafeInstallLocation);
        Directory.EnumerateFileSystemEntries(StagingRoot).Should().BeEmpty();
    }

    [Fact]
    public void IsSafeToReplace_Should_RefuseTheShellFolders_AndDriveRoots()
    {
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        UpdateInstaller.IsSafeToReplace(downloads).Should().BeFalse();
        UpdateInstaller.IsSafeToReplace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).Should().BeFalse();
        UpdateInstaller.IsSafeToReplace(Path.GetPathRoot(_root)!).Should().BeFalse();

        UpdateInstaller.IsSafeToReplace(InstallDirectory).Should().BeTrue();
    }

    [Fact]
    public async Task StageAsync_Should_Refuse_AnInstallUnderTheStagingRoot()
    {
        string nested = Path.Combine(StagingRoot, "v2.0.0", "unpacked");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "Helix.App.exe"), "binary");

        UpdateInstaller installer = new(
            new HttpClient(new StubHandler(() => Zip(("Helix.App.exe", "binary")))),
            NullLogger<UpdateInstaller>.Instance,
            () => nested,
            () => StagingRoot,
            () => LogDirectory);

        Result<string> result = await installer.StageAsync(Update());

        result.Error.Should().Be(UpdateErrors.UnsafeInstallLocation);

        File.Exists(Path.Combine(nested, "Helix.App.exe")).Should().BeTrue();
    }

    [Fact]
    public void SwapScript_Should_NotStartASecondInstance_WhileTheFirstIsStillRunning()
    {
        UpdateInstaller installer = Installer(() => Zip(("Helix.App.exe", "binary")));

        string staged = Path.Combine(StagingRoot, "v9.9.9", "unpacked");
        Directory.CreateDirectory(staged);

        string script = File.ReadAllText(installer.WriteSwapScript(staged, InstallDirectory));

        int stillRunning = script.IndexOf("was still running", StringComparison.Ordinal);
        int move = script.IndexOf("Move-Item", StringComparison.Ordinal);

        stillRunning.Should().BeGreaterThan(0);
        stillRunning.Should().BeLessThan(move);

        script.Should().Contain("-WorkingDirectory $install");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond());
    }

    private sealed class RoutedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    private UpdateInstaller Installer(Func<HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)),
            NullLogger<UpdateInstaller>.Instance,
            () => InstallDirectory,
            () => StagingRoot,
            () => LogDirectory);

    /// <summary>
    /// An installer that pins a key, answering the archive and the manifest by URL.
    /// </summary>
    private UpdateInstaller SigningInstaller(
        string publicKey,
        byte[] archive,
        Func<HttpResponseMessage>? manifest) =>
        new(new HttpClient(new RoutedHandler(request =>
                request.RequestUri!.AbsoluteUri == ManifestUrl
                    ? manifest?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : Respond(archive))),
            NullLogger<UpdateInstaller>.Instance,
            () => InstallDirectory,
            () => StagingRoot,
            () => LogDirectory,
            publicKey);

    private const string AssetName = "Helix-v2.1.0-win-x64.zip";

    private const string ManifestUrl = "https://example.invalid/signatures";

    private static UpdateCheck Update(
        string? url = "https://example.invalid/helix.zip",
        string? digest = null,
        string? signatureUrl = null) =>
        new(true, "2.0.0", "v2.1.0", "https://example.invalid/release", url, AssetName, digest, signatureUrl);

    private static (string PublicKey, string Signature) Sign(byte[] archive)
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        return (
            Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo()),
            Convert.ToBase64String(signer.SignHash(SHA256.HashData(archive), DSASignatureFormat.Rfc3279DerSequence)));
    }

    private static HttpResponseMessage Manifest(string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(text, Encoding.UTF8),
    };

    private static string DigestOf(byte[] archive) => $"sha256:{Convert.ToHexString(SHA256.HashData(archive))}";

    private static HttpResponseMessage Zip(params (string Name, string Content)[] entries) =>
        Respond(ZipBytes(entries));

    private static HttpResponseMessage Respond(byte[] body) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(body),
    };

    private static byte[] ZipBytes(params (string Name, string Content)[] entries)
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content) in entries)
            {
                using StreamWriter writer = new(archive.CreateEntry(name).Open());
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }

    [Fact]
    public async Task StageAsync_Should_UnpackTheBuild_WhenTheArchiveHoldsIt()
    {
        UpdateInstaller installer = Installer(() => Zip(("Helix.App.exe", "binary"), ("Helix.App.dll", "binary")));

        Result<string> result = await installer.StageAsync(Update());

        result.IsSuccess.Should().BeTrue();
        File.Exists(Path.Combine(result.Value, "Helix.App.exe")).Should().BeTrue();
    }

    [Fact]
    public async Task StageAsync_Should_FindTheBuild_WhenTheArchiveWrapsItInAFolder()
    {
        UpdateInstaller installer = Installer(() => Zip(("Helix-v2.1.0/Helix.App.exe", "binary")));

        Result<string> result = await installer.StageAsync(Update());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().EndWith("Helix-v2.1.0");
    }

    [Fact]
    public async Task StageAsync_Should_Refuse_AnArchiveThatIsNotHelix()
    {
        UpdateInstaller installer = Installer(() => Zip(("readme.txt", "not a build")));

        Result<string> result = await installer.StageAsync(Update());

        result.Error.Should().Be(UpdateErrors.DownloadNotHelix);
    }

    [Fact]
    public async Task StageAsync_Should_Refuse_SomethingThatIsNotAnArchive()
    {
        UpdateInstaller installer = Installer(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("404: Not Found", Encoding.UTF8),
        });

        Result<string> result = await installer.StageAsync(Update());

        result.Error.Should().Be(UpdateErrors.UnreadableDownload);
    }

    [Fact]
    public async Task StageAsync_Should_Refuse_WhenTheDownloadFails()
    {
        UpdateInstaller installer = Installer(() => new HttpResponseMessage(HttpStatusCode.NotFound));

        Result<string> result = await installer.StageAsync(Update());

        result.Error.Should().Be(UpdateErrors.DownloadFailed);
    }

    [Fact]
    public async Task StageAsync_Should_Refuse_WhenTheReleaseHasNothingForThisMachine()
    {
        UpdateInstaller installer = Installer(() => Zip(("Helix.App.exe", "binary")));

        Result<string> result = await installer.StageAsync(Update(url: null));

        result.Error.Should().Be(UpdateErrors.NoAsset);
    }

    [Fact]
    public async Task StageAsync_Should_ReportProgress()
    {
        UpdateInstaller installer = Installer(() => Zip(("Helix.App.exe", new string('x', 200_000))));

        List<double> reported = [];

        await installer.StageAsync(Update(), new Progress<double>(reported.Add));

        await Task.Delay(200);

        reported.Should().NotBeEmpty();
        reported.Should().OnlyContain(fraction => fraction > 0 && fraction <= 1);
    }

    [Fact]
    public async Task StageAsync_Should_LeaveTheInstallAlone_WhateverHappens()
    {
        File.WriteAllText(Path.Combine(InstallDirectory, "Helix.App.exe"), "the running build");

        UpdateInstaller installer = Installer(() => Zip(("readme.txt", "not a build")));

        await installer.StageAsync(Update());

        File.ReadAllText(Path.Combine(InstallDirectory, "Helix.App.exe")).Should().Be("the running build");
    }

    [Fact]
    public async Task StageAsync_Should_StartClean_WhenAnEarlierAttemptLeftAHalfUnpackedCopy()
    {
        string releaseDirectory = Path.Combine(StagingRoot, "v2.1.0");
        Directory.CreateDirectory(Path.Combine(releaseDirectory, "unpacked"));
        File.WriteAllText(Path.Combine(releaseDirectory, "unpacked", "leftover.txt"), "from a failed attempt");

        UpdateInstaller installer = Installer(() => Zip(("Helix.App.exe", "binary")));

        Result<string> result = await installer.StageAsync(Update());

        result.IsSuccess.Should().BeTrue();
        File.Exists(Path.Combine(result.Value, "leftover.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task StageAsync_Should_KeepTheDownloadInsideTheStagingFolder_WhateverTheAssetIsCalled()
    {
        UpdateInstaller installer = Installer(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not an archive", Encoding.UTF8),
        });

        var update = new UpdateCheck(
            true,
            "2.0.0",
            "v2.2.0",
            "https://example.invalid/release",
            "https://example.invalid/helix.zip",
            "../../escaped.zip");

        Result<string> result = await installer.StageAsync(update);

        result.Error.Should().Be(UpdateErrors.UnreadableDownload);

        Directory.GetFiles(_root, "escaped.zip", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task StageAsync_Should_KeepNothing_WhenTheArchiveWillNotUnpack()
    {
        UpdateInstaller installer = Installer(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not an archive", Encoding.UTF8),
        });

        Result<string> result = await installer.StageAsync(Update());

        result.Error.Should().Be(UpdateErrors.UnreadableDownload);

        Directory.Exists(Path.Combine(StagingRoot, "v2.1.0")).Should().BeFalse();
    }

    [Fact]
    public void Apply_Should_Refuse_AStagedFolderThatIsNotThere()
    {
        UpdateInstaller installer = Installer(() => Zip(("Helix.App.exe", "binary")));

        Result result = installer.Apply(Path.Combine(StagingRoot, "never-staged"));

        result.Error.Should().Be(UpdateErrors.UnreadableDownload);
    }

    [Fact]
    public void Helper_Should_NotRunFromInsideTheFolderItReplaces()
    {
        string scriptPath = Path.Combine(StagingRoot, "helper", "apply-update.ps1");

        ProcessStartInfo start = UpdateInstaller.CreateHelperStart(scriptPath);

        start.WorkingDirectory.Should().NotBeNullOrWhiteSpace();

        string workingDirectory = Path.GetFullPath(start.WorkingDirectory);
        string install = Path.GetFullPath(InstallDirectory);

        workingDirectory.Should().NotStartWith(install);
        workingDirectory.Should().Be(Path.GetFullPath(Path.GetDirectoryName(scriptPath)!));
    }

    [Fact]
    public void SwapScript_Should_RetryTheMove_AndRecordWhatHappened()
    {
        UpdateInstaller installer = Installer(() => Zip(("Helix.App.exe", "binary")));

        string staged = Path.Combine(StagingRoot, "v9.9.9", "unpacked");
        Directory.CreateDirectory(staged);

        string scriptPath = installer.WriteSwapScript(staged, InstallDirectory);

        string script = File.ReadAllText(scriptPath);

        script.Should().Contain("foreach ($attempt in 1..");
        script.Should().Contain("UpdateHelper:");

        script.Should().Contain(Path.Combine(LogDirectory, "helix-updates.log"));
    }

    [Fact]
    public async Task StageAsync_Should_AcceptTheDownload_WhenItMatchesThePublishedDigest()
    {
        byte[] archive = ZipBytes(("Helix.App.exe", "binary"));

        UpdateInstaller installer = Installer(() => Respond(archive));

        Result<string> result = await installer.StageAsync(Update(digest: DigestOf(archive)));

        result.IsSuccess.Should().BeTrue();
        File.Exists(Path.Combine(result.Value, "Helix.App.exe")).Should().BeTrue();
    }

    [Fact]
    public async Task StageAsync_Should_Refuse_WhenTheDownloadDoesNotMatchThePublishedDigest()
    {
        UpdateInstaller installer = Installer(() => Zip(("Helix.App.exe", "binary")));

        Result<string> result = await installer.StageAsync(
            Update(digest: $"sha256:{new string('a', 64)}"));

        result.Error.Should().Be(UpdateErrors.DownloadCorrupt);
    }

    [Fact]
    public async Task StageAsync_Should_KeepNothing_WhenTheDigestDoesNotMatch()
    {
        UpdateInstaller installer = Installer(() => Zip(("Helix.App.exe", "binary")));

        await installer.StageAsync(Update(digest: $"sha256:{new string('a', 64)}"));

        Directory.Exists(Path.Combine(StagingRoot, "v2.1.0")).Should().BeFalse();
    }

    [Fact]
    public async Task StageAsync_Should_Stage_WhenTheReleasePublishesNoDigest()
    {
        UpdateInstaller installer = Installer(() => Zip(("Helix.App.exe", "binary")));

        Result<string> result = await installer.StageAsync(Update(digest: null));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task StageAsync_Should_Stage_WhenTheDigestIsInAnAlgorithmItDoesNotKnow()
    {
        UpdateInstaller installer = Installer(() => Zip(("Helix.App.exe", "binary")));

        Result<string> result = await installer.StageAsync(Update(digest: "sha512:whatever"));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task StageAsync_Should_RemoveTheReleasesStagedBeforeIt()
    {
        string old = Path.Combine(StagingRoot, "v2.0.0", "unpacked");
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "Helix.App.exe"), "the version before last");

        UpdateInstaller installer = Installer(() => Zip(("Helix.App.exe", "binary")));

        await installer.StageAsync(Update());

        Directory.Exists(Path.Combine(StagingRoot, "v2.0.0")).Should().BeFalse();
        Directory.Exists(Path.Combine(StagingRoot, "v2.1.0")).Should().BeTrue();
    }

    [Fact]
    public async Task StageAsync_Should_LeaveTheHelperFolderAlone()
    {
        string helper = Path.Combine(StagingRoot, "helper");
        Directory.CreateDirectory(helper);
        File.WriteAllText(Path.Combine(helper, "apply-update.ps1"), "an update in progress");

        UpdateInstaller installer = Installer(() => Zip(("Helix.App.exe", "binary")));

        await installer.StageAsync(Update());

        File.Exists(Path.Combine(helper, "apply-update.ps1")).Should().BeTrue();
    }

    [Fact]
    public void SwapScript_Should_RemoveTheStagedRelease_OnceTheCopyHasSucceeded()
    {
        UpdateInstaller installer = Installer(() => Zip(("Helix.App.exe", "binary")));

        string release = Path.Combine(StagingRoot, "v2.1.0");
        string staged = Path.Combine(release, "unpacked");
        Directory.CreateDirectory(staged);

        string script = File.ReadAllText(installer.WriteSwapScript(staged, InstallDirectory));

        script.Should().Contain(release);

        int copied = script.IndexOf("Copy-Item", StringComparison.Ordinal);
        int removed = script.IndexOf("Remove-Item -LiteralPath $release", StringComparison.Ordinal);

        removed.Should().BeGreaterThan(copied);
    }

    [Fact]
    public void SwapScript_Should_DeleteNothing_WhenTheStagedFolderIsNotUnderTheStagingRoot()
    {
        UpdateInstaller installer = Installer(() => Zip(("Helix.App.exe", "binary")));

        string outside = Path.Combine(_root, "somewhere-else");
        Directory.CreateDirectory(outside);

        string script = File.ReadAllText(installer.WriteSwapScript(outside, InstallDirectory));

        script.Should().Contain("$release = ''");
    }

    [Fact]
    public void SwapScript_Should_CarryAByteOrderMark_SoPowerShellReadsItAsUtf8()
    {
        UpdateInstaller installer = Installer(() => Zip(("Helix.App.exe", "binary")));

        string staged = Path.Combine(StagingRoot, "v9.9.9", "unpacked");
        Directory.CreateDirectory(staged);

        byte[] bytes = File.ReadAllBytes(installer.WriteSwapScript(staged, InstallDirectory));

        bytes.Take(3).Should().Equal(Encoding.UTF8.GetPreamble());
    }

    [Fact]
    public async Task StageAsync_Should_Stage_WhenThisBuildPinsNoKey_AndTheReleaseIsUnsigned()
    {
        // Every build before a key exists, and the one that carries the key's release.
        UpdateInstaller installer = Installer(() => Zip(("Helix.App.exe", "binary")));

        Result<string> result = await installer.StageAsync(Update(signatureUrl: null));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task StageAsync_Should_Refuse_AnUnsignedRelease_OnceAKeyIsPinned()
    {
        byte[] archive = ZipBytes(("Helix.App.exe", "binary"));
        (string publicKey, _) = Sign(archive);

        UpdateInstaller installer = SigningInstaller(publicKey, archive, manifest: null);

        Result<string> result = await installer.StageAsync(Update(signatureUrl: null));

        result.Error.Should().Be(UpdateErrors.SignatureMissing);
        Directory.Exists(Path.Combine(StagingRoot, "v2.1.0")).Should().BeFalse();
    }

    [Fact]
    public async Task StageAsync_Should_Stage_WhenTheManifestSignsThisArchive()
    {
        byte[] archive = ZipBytes(("Helix.App.exe", "binary"));
        (string publicKey, string signature) = Sign(archive);

        // Windows line endings and another architecture's line, as a real manifest may carry.
        string manifest = $"Helix-v2.1.0-win-arm64.zip AAAA\r\n{AssetName} {signature}\r\n";

        UpdateInstaller installer = SigningInstaller(publicKey, archive, () => Manifest(manifest));

        Result<string> result = await installer.StageAsync(Update(signatureUrl: ManifestUrl));

        result.IsSuccess.Should().BeTrue();
        File.Exists(Path.Combine(result.Value, "Helix.App.exe")).Should().BeTrue();
    }

    [Fact]
    public async Task StageAsync_Should_Refuse_WhenTheSignatureIsForOtherBytes()
    {
        byte[] archive = ZipBytes(("Helix.App.exe", "binary"));
        (string publicKey, string signatureOfSomethingElse) = Sign(ZipBytes(("Helix.App.exe", "tampered")));

        UpdateInstaller installer = SigningInstaller(
            publicKey,
            archive,
            () => Manifest($"{AssetName} {signatureOfSomethingElse}"));

        Result<string> result = await installer.StageAsync(Update(signatureUrl: ManifestUrl));

        result.Error.Should().Be(UpdateErrors.SignatureInvalid);
        Directory.Exists(Path.Combine(StagingRoot, "v2.1.0")).Should().BeFalse();
    }

    [Fact]
    public async Task StageAsync_Should_Refuse_WhenTheManifestSignsOnlyOtherArchitectures()
    {
        byte[] archive = ZipBytes(("Helix.App.exe", "binary"));
        (string publicKey, string signature) = Sign(archive);

        UpdateInstaller installer = SigningInstaller(
            publicKey,
            archive,
            () => Manifest($"Helix-v2.1.0-win-arm64.zip {signature}"));

        Result<string> result = await installer.StageAsync(Update(signatureUrl: ManifestUrl));

        result.Error.Should().Be(UpdateErrors.SignatureMissing);
    }

    [Fact]
    public async Task StageAsync_Should_ReportANetworkFailure_WhenTheManifestCannotBeFetched()
    {
        byte[] archive = ZipBytes(("Helix.App.exe", "binary"));
        (string publicKey, _) = Sign(archive);

        UpdateInstaller installer = SigningInstaller(
            publicKey,
            archive,
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        Result<string> result = await installer.StageAsync(Update(signatureUrl: ManifestUrl));

        // Not "this release is not signed": it may well be, and the user should try again.
        result.Error.Should().Be(UpdateErrors.DownloadFailed);
    }

    [Fact]
    public async Task StageAsync_Should_PropagateCancellation_RatherThanReportItAsAnUnsignedRelease()
    {
        byte[] archive = ZipBytes(("Helix.App.exe", "binary"));
        (string publicKey, _) = Sign(archive);

        using var cancellation = new CancellationTokenSource();

        UpdateInstaller installer = SigningInstaller(publicKey, archive, () =>
        {
            cancellation.Cancel();

            throw new TaskCanceledException();
        });

        Func<Task> act = () => installer.StageAsync(Update(signatureUrl: ManifestUrl), null, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task StageAsync_Should_KeepNothing_WhenTheDownloadFails()
    {
        UpdateInstaller installer = Installer(() => new HttpResponseMessage(HttpStatusCode.NotFound));

        await installer.StageAsync(Update());

        Directory.Exists(Path.Combine(StagingRoot, "v2.1.0")).Should().BeFalse();
    }
}
