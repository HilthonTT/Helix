using Helix.Application.Abstractions.Updates;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Helix.Infrastructure.Updates;

/// <summary>
/// Downloads a release archive, unpacks it beside the install, and hands the swap to a
/// helper script that outlives the process.
/// </summary>
/// <remarks>
/// A process cannot replace the files it is running from, so the last step has to be done
/// by something else. The helper waits for this process to exit, backs the install up,
/// copies the new version over it, restores the backup if that fails, and starts Helix
/// again. Everything before that — download, unpack, sanity check — happens while Helix
/// is still running and leaves the install untouched if any of it fails.
///
/// Nothing here verifies a signature, because there is nothing to verify against: the
/// release archives are unsigned. What it does have is TLS to github.com, the SHA-256
/// the release API publishes for the asset, and a check that the archive contains the
/// executable it claims to. That is the same trust the user extends by downloading the
/// zip by hand, which is what this replaces, plus the one check they would not have
/// made.
/// </remarks>
internal sealed class UpdateInstaller : IUpdateInstaller
{
    /// <summary>Where staged downloads are kept, under the per-user app data directory.</summary>
    private const string StagingDirectoryName = "updates";

    /// <summary>The file the unpacked archive must contain to be believed.</summary>
    private const string WindowsExecutable = "Helix.App.exe";

    /// <summary>How long the helper waits for Helix to exit before giving up.</summary>
    private const int ExitWaitSeconds = 60;

    /// <summary>
    /// How many times the helper tries to move the install aside before giving up.
    /// </summary>
    /// <remarks>
    /// One attempt was enough to lose an update to whatever happened to be looking at the
    /// folder for a second — an indexer, a virus scanner, an Explorer window left open in
    /// it. A second a try for ten seconds costs nothing when the alternative is a release
    /// that silently does not install.
    /// </remarks>
    private const int MoveAttempts = 10;

    /// <summary>
    /// The file the helper writes what it did to, in the folder the diagnostics export
    /// reads.
    /// </summary>
    /// <remarks>
    /// The helper runs after Helix has exited, so it cannot log through
    /// <see cref="ILogger"/> like everything else. It is also the one part of the update
    /// nobody watches: a swap that fails puts the previous version back and starts it,
    /// which is indistinguishable from a swap that worked unless it says so somewhere.
    /// Named to match what <c>LogFileWriter</c> collects, so it lands in the zip the user
    /// is asked to send without anything else having to know about it.
    /// </remarks>
    private const string HelperLogFileName = "helix-updates.log";

    /// <summary>The only digest algorithm GitHub publishes, and the only one understood.</summary>
    private const string DigestPrefix = "sha256:";

    /// <summary>The staging root's one subfolder that is not a staged release.</summary>
    private const string HelperDirectoryName = "helper";

    private readonly HttpClient _httpClient;
    private readonly ILogger<UpdateInstaller> _logger;
    private readonly Func<string> _installDirectory;
    private readonly Func<string> _stagingRoot;
    private readonly Func<string> _logDirectory;

    /// <param name="installDirectory">
    /// The folder to be replaced. Injected rather than read inline so a test can point it
    /// at a temporary directory instead of the running app.
    /// </param>
    /// <param name="logDirectory">
    /// Where the helper writes what it did, since it outlives the logger.
    /// </param>
    public UpdateInstaller(
        HttpClient httpClient,
        ILogger<UpdateInstaller> logger,
        Func<string> installDirectory,
        Func<string> stagingRoot,
        Func<string> logDirectory)
    {
        _httpClient = httpClient;
        _logger = logger;
        _installDirectory = installDirectory;
        _stagingRoot = stagingRoot;
        _logDirectory = logDirectory;
    }

    /// <summary>Where staged downloads go, beside the database and the logs.</summary>
    public static string DefaultStagingRoot =>
        Path.Combine(FileSystem.AppDataDirectory, StagingDirectoryName);

    /// <summary>
    /// The folder that would be replaced: the directory the app runs from.
    /// </summary>
    /// <remarks>
    /// On macOS this has to be the <c>.app</c> bundle rather than the directory the
    /// assemblies sit in, because the bundle is the unit that gets replaced — and the
    /// binary runs several levels inside it.
    /// </remarks>
    public static string DefaultInstallDirectory
    {
        get
        {
#if MACCATALYST
            return Platform.MacBundle.BundlePath;
#else
            return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
#endif
        }
    }

    public bool IsSupported
    {
        get
        {
            try
            {
                return CanWriteTo(_installDirectory());
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not work out whether the install folder is writable.");

                return false;
            }
        }
    }

    public async Task<Result<string>> StageAsync(
        UpdateCheck update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(update.DownloadUrl))
        {
            return Result.Failure<string>(UpdateErrors.NoAsset);
        }

        if (!IsSupported)
        {
            return Result.Failure<string>(UpdateErrors.NotWritable);
        }

        string releaseDirectory = Path.Combine(_stagingRoot(), Sanitize(update.LatestVersion));

        // Anything else under the staging root is a release that was installed or given
        // up on, and each one is an unpacked build of a couple of hundred megabytes. They
        // used to stay there for the life of the install: one folder per version the user
        // had ever updated through, in the same per-user app data directory as the
        // database and the logs.
        PruneStagedReleases(releaseDirectory);

        try
        {
            // A previous attempt's half-unpacked copy is not something to build on.
            if (Directory.Exists(releaseDirectory))
            {
                Directory.Delete(releaseDirectory, recursive: true);
            }

            Directory.CreateDirectory(releaseDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not prepare the staging folder for the update.");

            return Result.Failure<string>(UpdateErrors.DownloadFailed);
        }

        // Named after the asset for the log's sake, but never trusted as a path: the
        // name is whatever the release carries, and Path.Combine with something holding
        // a separator or a parent-directory segment would write outside the staging
        // folder.
        string archivePath = Path.Combine(releaseDirectory, ArchiveFileName(update.AssetName));

        Result<string> download = await DownloadAsync(update.DownloadUrl, archivePath, progress, cancellationToken);
        if (download.IsFailure)
        {
            return Result.Failure<string>(download.Error);
        }

        // Before it is opened, not after: an archive that is not what GitHub published is
        // not something to unpack and inspect, it is something to throw away.
        Result verified = VerifyDigest(update, download.Value);
        if (verified.IsFailure)
        {
            TryDeleteDirectory(releaseDirectory);

            return Result.Failure<string>(verified.Error);
        }

        string unpacked = Path.Combine(releaseDirectory, "unpacked");

        try
        {
            ZipFile.ExtractToDirectory(archivePath, unpacked, overwriteFiles: true);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The downloaded update could not be unpacked.");

            TryDeleteDirectory(releaseDirectory);

            return Result.Failure<string>(UpdateErrors.UnreadableDownload);
        }

        // The archive may hold the payload directly or wrap it in one folder — the Mac
        // build is a .app bundle, which is itself a directory.
        string? payload = ResolvePayload(unpacked);
        if (payload is null)
        {
            _logger.LogWarning("The downloaded update did not contain a Helix build.");

            TryDeleteDirectory(releaseDirectory);

            return Result.Failure<string>(UpdateErrors.DownloadNotHelix);
        }

        // The download is only wanted for as long as it takes to unpack; a release is
        // a couple of hundred megabytes and there is no reason to leave two copies.
        TryDelete(archivePath);

        _logger.LogInformation("Update {Version} staged and ready to install.", update.LatestVersion);

        return payload;
    }

    public Result Apply(string stagedDirectory)
    {
        if (!Directory.Exists(stagedDirectory))
        {
            return Result.Failure(UpdateErrors.UnreadableDownload);
        }

        if (!IsSupported)
        {
            return Result.Failure(UpdateErrors.NotWritable);
        }

        try
        {
            string install = _installDirectory();
            string scriptPath = WriteSwapScript(stagedDirectory, install);

            using Process? helper = Process.Start(CreateHelperStart(scriptPath));

            if (helper is null)
            {
                return Result.Failure(UpdateErrors.InstallFailed("the helper process did not start"));
            }

            _logger.LogInformation("Handed the update swap to the helper; quitting to let it run.");

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not start the update helper.");

            return Result.Failure(UpdateErrors.InstallFailed(ex.Message));
        }
    }

    /// <summary>
    /// Fetches the archive, and reports the SHA-256 of exactly the bytes that were
    /// written.
    /// </summary>
    /// <remarks>
    /// Hashed as it is copied rather than by reading the file back afterwards: the
    /// archive is a couple of hundred megabytes, the bytes are already in hand, and a
    /// second pass over a file that was just written measures the disk cache more than
    /// anything else.
    /// </remarks>
    private async Task<Result<string>> DownloadAsync(
        string url,
        string destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "The update download answered with status {Status}.",
                    (int)response.StatusCode);

                return Result.Failure<string>(UpdateErrors.DownloadFailed);
            }

            long? total = response.Content.Headers.ContentLength;

            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using FileStream target = File.Create(destination);

            using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            byte[] buffer = new byte[81920];
            long copied = 0;
            int read;

            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

                digest.AppendData(buffer, 0, read);

                copied += read;

                // Reported only when the server said how much there is; a progress bar
                // that cannot know its end is worse than one that does not move.
                if (total is > 0)
                {
                    progress?.Report((double)copied / total.Value);
                }
            }

            return Convert.ToHexString(digest.GetHashAndReset());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The user changed their mind; nothing has been touched. Filtered on the
            // caller's token because HttpClient reports its own timeout as the same
            // exception, and that one is a failed download, not a change of mind.
            TryDelete(destination);

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The update download failed.");

            TryDelete(destination);

            return Result.Failure<string>(UpdateErrors.DownloadFailed);
        }
    }

    /// <summary>
    /// Checks what arrived against the digest GitHub published for it.
    /// </summary>
    /// <remarks>
    /// The archives are unsigned, so this proves nothing about who built them — what it
    /// proves is that the bytes are the ones the API described, which is the failure this
    /// can actually have: a transfer that stopped short, a proxy or a cache that served
    /// something else. It is the only check between a download and a working install
    /// being replaced by it.
    ///
    /// A release with no digest is accepted rather than refused. GitHub only began
    /// returning the field recently, and every release published before that has none;
    /// refusing them would break updating for exactly the installs most in need of it.
    /// The same goes for a digest in an algorithm this does not know: it cannot be
    /// checked, and turning that into a hard failure would mean a future change at
    /// GitHub's end stopping every install at once.
    /// </remarks>
    private Result VerifyDigest(UpdateCheck update, string actualDigest)
    {
        string? published = update.AssetDigest?.Trim();

        if (string.IsNullOrWhiteSpace(published))
        {
            _logger.LogInformation(
                "Release {Version} publishes no digest for {Asset}; the download could not be verified.",
                update.LatestVersion,
                update.AssetName);

            return Result.Success();
        }

        if (!published.StartsWith(DigestPrefix, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "The digest published for {Asset} is not one this build understands, so it was not checked.",
                update.AssetName);

            return Result.Success();
        }

        string expected = published[DigestPrefix.Length..];

        if (!string.Equals(expected, actualDigest, StringComparison.OrdinalIgnoreCase))
        {
            // Both are logged: a mismatch is either a broken download or something worth
            // being able to describe afterwards, and neither is diagnosable from the fact
            // that it happened.
            _logger.LogError(
                "The update downloaded for {Version} hashed to {Actual}, but GitHub published {Expected}. It was discarded.",
                update.LatestVersion,
                actualDigest,
                expected);

            return Result.Failure(UpdateErrors.DownloadCorrupt);
        }

        _logger.LogInformation("The download for {Version} matches the digest GitHub published.", update.LatestVersion);

        return Result.Success();
    }

    /// <summary>
    /// Removes every staged release except the one about to be used.
    /// </summary>
    /// <remarks>
    /// Done here rather than after an install because this is the moment the staging root
    /// is known to be nobody's working directory: the helper of a previous update copies
    /// out of a staged folder and would not survive it being deleted underneath it. Its
    /// own folder is left alone for the same reason - a helper started seconds ago may
    /// still be running from it.
    /// </remarks>
    private void PruneStagedReleases(string keep)
    {
        string root = _stagingRoot();

        if (!Directory.Exists(root))
        {
            return;
        }

        string keepName = Path.GetFileName(keep.TrimEnd(Path.DirectorySeparatorChar));

        foreach (string directory in Directory.EnumerateDirectories(root))
        {
            string name = Path.GetFileName(directory);

            if (string.Equals(name, keepName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, HelperDirectoryName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryDeleteDirectory(directory))
            {
                _logger.LogInformation("Removed the staged copy of a previous update at {Directory}.", name);
            }
        }
    }

    /// <summary>
    /// The staged release folder holding <paramref name="stagedDirectory"/>, for the
    /// helper to remove once it has finished copying out of it.
    /// </summary>
    /// <remarks>
    /// The payload is a folder or two below the release folder - <c>unpacked</c>, and on
    /// macOS the <c>.app</c> bundle inside it - so what has to be deleted is not what was
    /// handed over. Found by climbing to the child of the staging root rather than by
    /// taking the path apart, and anything that is not under the staging root at all is
    /// left exactly as it is: a caller pointing somewhere else means a test, and a
    /// delete that walked out of the staging root would be the worst bug in this file.
    /// </remarks>
    private string? ReleaseFolderOf(string stagedDirectory)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_stagingRoot()));
        var candidate = new DirectoryInfo(Path.GetFullPath(stagedDirectory));

        while (candidate.Parent is not null)
        {
            if (string.Equals(
                Path.TrimEndingDirectorySeparator(candidate.Parent.FullName),
                root,
                StringComparison.OrdinalIgnoreCase))
            {
                return candidate.FullName;
            }

            candidate = candidate.Parent;
        }

        return null;
    }

    /// <summary>
    /// The folder inside the unpacked archive that actually holds the new build, or null
    /// if there is nothing recognisable in it.
    /// </summary>
    private static string? ResolvePayload(string unpacked)
    {
        if (LooksLikeHelix(unpacked))
        {
            return unpacked;
        }

        // ditto and most zip tools wrap the payload in a single folder — on macOS that
        // folder is the .app bundle itself.
        foreach (string candidate in Directory.EnumerateDirectories(unpacked))
        {
            if (LooksLikeHelix(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a folder holds something that can replace the running install.
    /// </summary>
    /// <remarks>
    /// The last thing standing between a corrupt or unexpected archive and a working
    /// install being copied over. Deliberately shallow: it asks whether the executable is
    /// there, not whether the build is genuine, which is not a question an unsigned
    /// archive can answer.
    /// </remarks>
    private static bool LooksLikeHelix(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        if (File.Exists(Path.Combine(directory, WindowsExecutable)))
        {
            return true;
        }

        // A .app bundle: a directory with the standard Contents/MacOS layout inside it.
        return directory.EndsWith(".app", StringComparison.OrdinalIgnoreCase) &&
               Directory.Exists(Path.Combine(directory, "Contents", "MacOS"));
    }

    /// <summary>Whether the current user can actually write where Helix is installed.</summary>
    /// <remarks>
    /// Asked by writing, not by reading permissions: the answer that matters is what the
    /// filesystem does, and every other way of asking is an approximation of it.
    /// </remarks>
    private static bool CanWriteTo(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        string probe = Path.Combine(directory, $".helix-write-probe-{Guid.NewGuid():N}");

        try
        {
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
                return true;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// How the helper is launched. The working directory is the load-bearing part.
    /// </summary>
    /// <remarks>
    /// Left unset, a child process inherits this one's current directory — and Helix's is
    /// the install folder: Explorer starts an app there, and the shortcuts the startup and
    /// desktop services write set it there explicitly. A process whose current directory
    /// is a folder holds a handle to it, and Windows will not rename a folder that is
    /// held, so <c>Move-Item $install $install.old</c> failed with a sharing violation
    /// every single time. The script's catch swallowed it and its last line started the
    /// old build again: the update reported success, the app came back, and it was still
    /// the version it had been. Pointing the helper at its own folder — under app data,
    /// never inside the install — is what makes the swap possible at all.
    ///
    /// PowerShell's <c>Set-Location</c> inside the script would not do this: it moves the
    /// shell's location, not the process's current directory, and it is the process's
    /// handle that holds the folder.
    /// </remarks>
    internal static ProcessStartInfo CreateHelperStart(string scriptPath)
    {
        string helperDirectory = Path.GetDirectoryName(scriptPath) ?? Path.GetTempPath();

#if WINDOWS
        return new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = helperDirectory,
        };
#else
        return new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"\"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = helperDirectory,
        };
#endif
    }

    /// <summary>
    /// Writes the script that does the swap once this process is gone.
    /// </summary>
    /// <remarks>
    /// The script waits for this process to exit — its own files are what is about to be
    /// replaced — and then moves the install aside rather than writing over it, so a
    /// failure halfway can put back exactly what was there. Only once the copy has
    /// finished is the moved-aside copy deleted. An update that did not happen is a great
    /// deal better than a Helix that will not start.
    ///
    /// Both scripts are built line by line rather than as raw string literals, and
    /// neither carries a comment of its own. A raw literal would be the obvious way to
    /// write them, but each lives in a branch that is excluded on the other platform, and
    /// the preprocessor still scans excluded regions for directives: any line whose first
    /// non-whitespace character is <c>#</c> — a shell comment, a shebang — reads as one
    /// and fails the build for the other head.
    /// </remarks>
    internal string WriteSwapScript(string stagedDirectory, string installDirectory)
    {
        int processId = Environment.ProcessId;

        string scriptDirectory = Path.Combine(_stagingRoot(), "helper");
        Directory.CreateDirectory(scriptDirectory);

        // Created here rather than left to the helper: its logging is best-effort and
        // silent when it fails, and a missing folder would make it silent every time.
        string logDirectory = _logDirectory();
        Directory.CreateDirectory(logDirectory);

        string logPath = Path.Combine(logDirectory, HelperLogFileName);

        // Empty where the staged folder is not under the staging root, which is a test
        // pointing somewhere of its own; both scripts guard on it before deleting.
        string releaseDirectory = ReleaseFolderOf(stagedDirectory) ?? string.Empty;

#if WINDOWS
        string scriptPath = Path.Combine(scriptDirectory, "apply-update.ps1");
        string executable = Path.Combine(installDirectory, WindowsExecutable);

        string[] lines =
        [
            "$ErrorActionPreference = 'Stop'",
            $"$staged  = '{Escape(stagedDirectory)}'",
            $"$install = '{Escape(installDirectory)}'",
            $"$exe     = '{Escape(executable)}'",
            $"$log     = '{Escape(logPath)}'",
            $"$release = '{Escape(releaseDirectory)}'",
            "$backup  = \"$install.old\"",
            "function Write-Log($message) {",
            "    try {",
            "        $stamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss.fff')",
            "        Add-Content -LiteralPath $log -Value \"$stamp [inf] UpdateHelper: $message\"",
            "    } catch { }",
            "}",
            $"try {{ Wait-Process -Id {processId} -Timeout {ExitWaitSeconds} -ErrorAction Stop }} catch {{ }}",

            // The process object is gone before every handle it held is.
            "Start-Sleep -Seconds 2",
            "if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force }",
            "$moved = $false",
            "$reason = 'the folder was still held after every attempt'",
            $"foreach ($attempt in 1..{MoveAttempts}) {{",
            "    try {",
            "        Move-Item -LiteralPath $install -Destination $backup -Force",
            "        $moved = $true",
            "        break",
            "    } catch {",
            "        $reason = $_.Exception.Message",
            "        Start-Sleep -Seconds 1",
            "    }",
            "}",
            "if ($moved) {",
            "    try {",
            "        New-Item -ItemType Directory -Path $install -Force | Out-Null",
            "        Get-ChildItem -LiteralPath $staged -Force | Copy-Item -Destination $install -Recurse -Force",
            "        Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction SilentlyContinue",

            // Only once the copy has succeeded: until then it is the only copy of the
            // new version, and on the failure path below it is not needed either - what
            // goes back is the build that was already there.
            "        if ($release) { Remove-Item -LiteralPath $release -Recurse -Force -ErrorAction SilentlyContinue }",
            "        Write-Log 'The update was applied.'",
            "    } catch {",
            "        Write-Log \"The update could not be copied over the install: $($_.Exception.Message). The previous version was put back.\"",
            "        if (Test-Path -LiteralPath $install) {",
            "            Remove-Item -LiteralPath $install -Recurse -Force -ErrorAction SilentlyContinue",
            "        }",
            "        Move-Item -LiteralPath $backup -Destination $install -Force",
            "    }",
            "} else {",
            "    Write-Log \"The install folder could not be moved aside, so the update was not applied and the previous version is still installed: $reason\"",
            "}",
            "if (Test-Path -LiteralPath $exe) { Start-Process -FilePath $exe }",
        ];
#else
        string scriptPath = Path.Combine(scriptDirectory, "apply-update.sh");

        string[] lines =
        [
            $"staged=\"{Escape(stagedDirectory)}\"",
            $"install=\"{Escape(installDirectory)}\"",
            $"log=\"{Escape(logPath)}\"",
            $"release=\"{Escape(releaseDirectory)}\"",
            "backup=\"$install.old\"",
            "write_log() { printf '%s [inf] UpdateHelper: %s\\n' \"$(date -u '+%Y-%m-%d %H:%M:%S.000')\" \"$1\" >> \"$log\" 2>/dev/null || true; }",
            $"for _ in $(seq 1 {ExitWaitSeconds}); do",
            $"  kill -0 {processId} 2>/dev/null || break",
            "  sleep 1",
            "done",
            "sleep 2",
            "rm -rf \"$backup\"",
            "if mv \"$install\" \"$backup\"; then",

            // ditto rather than cp: a .app is symlinks, permissions and extended
            // attributes, and a plain copy flattens the ones that make it launchable.
            "  if ditto \"$staged\" \"$install\"; then",
            "    rm -rf \"$backup\"",
            "    [ -n \"$release\" ] && rm -rf \"$release\"",
            "    write_log 'The update was applied.'",
            "  else",
            "    rm -rf \"$install\"",
            "    mv \"$backup\" \"$install\"",
            "    write_log 'The update could not be copied over the install. The previous version was put back.'",
            "  fi",
            "else",
            "  write_log 'The install folder could not be moved aside, so the update was not applied and the previous version is still installed.'",
            "fi",
            "open \"$install\"",
        ];
#endif

        File.WriteAllText(scriptPath, string.Join(Environment.NewLine, lines));

        return scriptPath;
    }

    /// <summary>Closes a path against the quoting of whichever shell runs the script.</summary>
    private static string Escape(string path) =>
#if WINDOWS
        path.Replace("'", "''");
#else
        path.Replace("\"", "\\\"");
#endif

    /// <summary>
    /// A safe file name for the downloaded archive, whatever the release called it.
    /// </summary>
    private static string ArchiveFileName(string? assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName))
        {
            return "update.zip";
        }

        // GetFileName strips any directory part; Sanitize deals with the rest, and a
        // name that was nothing but a path separator leaves nothing behind.
        string name = Sanitize(Path.GetFileName(assetName));

        return string.IsNullOrWhiteSpace(name) ? "update.zip" : name;
    }

    private static string Sanitize(string version) =>
        string.Concat(version.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    /// <summary>Removes a directory tree, reporting whether it went.</summary>
    private bool TryDeleteDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return false;
            }

            Directory.Delete(path, recursive: true);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Something is holding it - an open Explorer window, a scanner. It is a
            // couple of hundred megabytes of disk, not a reason to fail an update.
            _logger.LogDebug(ex, "Could not remove the staged update folder {Directory}.", path);

            return false;
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not remove a staged update file.");
        }
    }
}
