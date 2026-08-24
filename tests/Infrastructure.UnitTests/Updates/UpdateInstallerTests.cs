using FluentAssertions;
using Helix.Application.Abstractions.Updates;
using Helix.Infrastructure.Updates;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
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

    private static UpdateCheck Update(string? url = "https://example.invalid/helix.zip") =>
        new(true, "2.0.0", "v2.1.0", "https://example.invalid/release", url, "Helix-v2.1.0-win-x64.zip");

    private static HttpResponseMessage Zip(params (string Name, string Content)[] entries)
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

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(buffer.ToArray()),
        };
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
        // Staged from something that will not unpack on purpose: that is the one path
        // that leaves the downloaded file on disk to be found, where a run that succeeds
        // or fails to download deletes it and would hide where it had been written.
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

        File.Exists(Path.Combine(_root, "escaped.zip")).Should().BeFalse();
        Directory.GetFiles(StagingRoot, "escaped.zip", SearchOption.AllDirectories).Should().ContainSingle();
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
}
