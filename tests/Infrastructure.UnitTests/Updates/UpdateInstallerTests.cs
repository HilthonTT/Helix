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

/// <summary>
/// Covers staging — the half of the updater that runs while Helix does.
/// </summary>
/// <remarks>
/// The swap itself is not covered here and cannot usefully be: it happens in a helper
/// process, after this one has exited, against the folder the test runner is running
/// from. What is worth pinning down is everything that decides whether that helper is
/// ever started, because each of these failures must leave the install untouched.
/// </remarks>
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

        // What makes the folder an install: the swap replaces the whole folder, so one
        // that does not hold the executable is refused before anything is downloaded.
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
        // "Extract here" on the release zip in Downloads puts Helix.App.exe straight into
        // Downloads, and the swap would then replace Downloads.
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

        // Not pruned out from under itself.
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

        // The restarted app must not inherit the helper's working directory.
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
            // A test left a handle open; the temp folder is the OS's problem after that.
        }
    }

    /// <summary>Answers the download with whatever bytes the test wants.</summary>
    private sealed class StubHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond());
    }

    private UpdateInstaller Installer(Func<HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)),
            NullLogger<UpdateInstaller>.Instance,
            () => InstallDirectory,
            () => StagingRoot,
            () => LogDirectory);

    private static UpdateCheck Update(
        string? url = "https://example.invalid/helix.zip",
        string? digest = null) =>
        new(true, "2.0.0", "v2.1.0", "https://example.invalid/release", url, "Helix-v2.1.0-win-x64.zip", digest);

    /// <summary>The digest GitHub would publish for these bytes.</summary>
    private static string DigestOf(byte[] archive) => $"sha256:{Convert.ToHexString(SHA256.HashData(archive))}";

    private static HttpResponseMessage Zip(params (string Name, string Content)[] entries) =>
        Respond(ZipBytes(entries));

    private static HttpResponseMessage Respond(byte[] body) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(body),
    };

    /// <summary>
    /// A zip's bytes, kept rather than wrapped straight into a response.
    /// </summary>
    /// <remarks>
    /// Every entry is stamped with the time it was created, so two calls do not produce
    /// the same archive — a test that checks a digest has to hash the same bytes it
    /// serves, not an identical-looking zip built a moment later.
    /// </remarks>
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
        // Most zip tools wrap the payload; the Mac archive's wrapper is the .app itself.
        UpdateInstaller installer = Installer(() => Zip(("Helix-v2.1.0/Helix.App.exe", "binary")));

        Result<string> result = await installer.StageAsync(Update());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().EndWith("Helix-v2.1.0");
    }

    [Fact]
    public async Task StageAsync_Should_Refuse_AnArchiveThatIsNotHelix()
    {
        // The last thing between a wrong or corrupt download and a working install being
        // copied over.
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

        // Progress<T> hands its callbacks to the synchronization context, which a test has
        // none of, so they arrive on the thread pool — give them a moment to land.
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

        // The whole design rests on this: nothing before Apply touches what is installed.
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
        // The asset name is whatever the release carries. Combined into a path unchecked,
        // a name with a parent-directory segment in it writes outside the staging folder.
        //
        // Staged from something that will not unpack on purpose, so that the download is
        // known to have run to completion and written its file somewhere: a test that
        // only checked the absence of an escaped file would pass just as well if nothing
        // had been downloaded at all.
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

        // Nowhere above the staging root, and nowhere beside it either. The archive
        // itself is no longer there to be found - a staging attempt that fails now
        // clears up after itself, which is the whole point of the folder not outliving
        // the attempt - so what is checked is the escape rather than the arrival.
        Directory.GetFiles(_root, "escaped.zip", SearchOption.AllDirectories).Should().BeEmpty();
    }

    /// <summary>
    /// A staging attempt that fails does not leave its download behind.
    /// </summary>
    /// <remarks>
    /// The archive is the same couple of hundred megabytes whether it unpacked or not,
    /// and an install that is never going to happen has no claim on the disk. The next
    /// attempt at the same version would clear the folder anyway; this is about the
    /// attempt that is never repeated.
    /// </remarks>
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

    /// <summary>
    /// The helper must not be started inside the folder it has to rename.
    /// </summary>
    /// <remarks>
    /// This is the whole of the bug that shipped an update which did not install. A child
    /// process started with no working directory inherits the parent's, which for Helix is
    /// the install folder — Explorer starts an app there, and both shortcut services set
    /// it there by hand. A process holds a handle to its current directory, Windows will
    /// not rename a held folder, and the swap failed every time while reporting success.
    /// </remarks>
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

    /// <summary>
    /// A swap that cannot happen has to be retried, and then said out loud.
    /// </summary>
    /// <remarks>
    /// The failure path puts the previous version back and starts it, which looks exactly
    /// like a successful update from the outside — so the only thing separating "installed"
    /// from "silently did nothing" is what the helper writes down.
    /// </remarks>
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

        // Written where the diagnostics export looks, so a failed update reaches whoever
        // is asked to explain it.
        script.Should().Contain(Path.Combine(LogDirectory, "helix-updates.log"));
    }

    /// <summary>
    /// The download is checked against the digest GitHub published for it before anything
    /// is opened.
    /// </summary>
    [Fact]
    public async Task StageAsync_Should_AcceptTheDownload_WhenItMatchesThePublishedDigest()
    {
        byte[] archive = ZipBytes(("Helix.App.exe", "binary"));

        UpdateInstaller installer = Installer(() => Respond(archive));

        Result<string> result = await installer.StageAsync(Update(digest: DigestOf(archive)));

        result.IsSuccess.Should().BeTrue();
        File.Exists(Path.Combine(result.Value, "Helix.App.exe")).Should().BeTrue();
    }

    /// <summary>
    /// And thrown away when it does not. TLS covers who it came from; this covers whether
    /// all of it arrived, which is the failure a two-hundred-megabyte download actually
    /// has.
    /// </summary>
    [Fact]
    public async Task StageAsync_Should_Refuse_WhenTheDownloadDoesNotMatchThePublishedDigest()
    {
        UpdateInstaller installer = Installer(() => Zip(("Helix.App.exe", "binary")));

        Result<string> result = await installer.StageAsync(
            Update(digest: $"sha256:{new string('a', 64)}"));

        result.Error.Should().Be(UpdateErrors.DownloadCorrupt);
    }

    /// <summary>
    /// A refused download leaves nothing behind either — there is no reason to keep two
    /// hundred megabytes of an archive that will never be installed.
    /// </summary>
    [Fact]
    public async Task StageAsync_Should_KeepNothing_WhenTheDigestDoesNotMatch()
    {
        UpdateInstaller installer = Installer(() => Zip(("Helix.App.exe", "binary")));

        await installer.StageAsync(Update(digest: $"sha256:{new string('a', 64)}"));

        Directory.Exists(Path.Combine(StagingRoot, "v2.1.0")).Should().BeFalse();
    }

    /// <summary>
    /// Releases published before GitHub returned digests carry none, and refusing them
    /// would break updating for exactly the installs furthest behind.
    /// </summary>
    [Fact]
    public async Task StageAsync_Should_Stage_WhenTheReleasePublishesNoDigest()
    {
        UpdateInstaller installer = Installer(() => Zip(("Helix.App.exe", "binary")));

        Result<string> result = await installer.StageAsync(Update(digest: null));

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// Nor is a digest in an algorithm this build does not know a hard failure: it cannot
    /// be checked, and treating that as corruption would let one change at GitHub's end
    /// stop every install at once.
    /// </summary>
    [Fact]
    public async Task StageAsync_Should_Stage_WhenTheDigestIsInAnAlgorithmItDoesNotKnow()
    {
        UpdateInstaller installer = Installer(() => Zip(("Helix.App.exe", "binary")));

        Result<string> result = await installer.StageAsync(Update(digest: "sha512:whatever"));

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// Staging a release clears out the ones staged before it.
    /// </summary>
    /// <remarks>
    /// Each is an unpacked build of a couple of hundred megabytes, and they used to stay
    /// for the life of the install — one folder per version the user had ever updated
    /// through, in the same per-user directory as the database and the logs.
    /// </remarks>
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

    /// <summary>
    /// The helper's own folder is not one of them: a helper started seconds ago may still
    /// be running out of it.
    /// </summary>
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

    /// <summary>
    /// The one staged copy nothing else can clear is the one being installed: it is what
    /// the helper is copying out of, so only the helper knows when it is finished with.
    /// </summary>
    [Fact]
    public void SwapScript_Should_RemoveTheStagedRelease_OnceTheCopyHasSucceeded()
    {
        UpdateInstaller installer = Installer(() => Zip(("Helix.App.exe", "binary")));

        string release = Path.Combine(StagingRoot, "v2.1.0");
        string staged = Path.Combine(release, "unpacked");
        Directory.CreateDirectory(staged);

        string script = File.ReadAllText(installer.WriteSwapScript(staged, InstallDirectory));

        // The release folder, not the payload inside it: on macOS the payload is the .app
        // bundle a level further down, and leaving its parents behind would leave the
        // problem behind.
        script.Should().Contain(release);

        int copied = script.IndexOf("Copy-Item", StringComparison.Ordinal);
        int removed = script.IndexOf("Remove-Item -LiteralPath $release", StringComparison.Ordinal);

        removed.Should().BeGreaterThan(copied);
    }

    /// <summary>
    /// A staged folder that is not under the staging root belongs to nobody this can
    /// reason about, and is left exactly where it is.
    /// </summary>
    [Fact]
    public void SwapScript_Should_DeleteNothing_WhenTheStagedFolderIsNotUnderTheStagingRoot()
    {
        UpdateInstaller installer = Installer(() => Zip(("Helix.App.exe", "binary")));

        string outside = Path.Combine(_root, "somewhere-else");
        Directory.CreateDirectory(outside);

        string script = File.ReadAllText(installer.WriteSwapScript(outside, InstallDirectory));

        script.Should().Contain("$release = ''");
    }
}
