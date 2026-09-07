using Helix.Application.Abstractions.Updates;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Helix.Infrastructure.Updates;

internal sealed class UpdateInstaller : IUpdateInstaller
{
    private const string StagingDirectoryName = "updates";

    private const string WindowsExecutable = "Helix.App.exe";

    private const int ExitWaitSeconds = 60;

    private const int MoveAttempts = 10;

    private const string HelperLogFileName = "helix-updates.log";

    private const string DigestPrefix = "sha256:";

    private const string HelperDirectoryName = "helper";

    private readonly HttpClient _httpClient;
    private readonly ILogger<UpdateInstaller> _logger;
    private readonly Func<string> _installDirectory;
    private readonly Func<string> _stagingRoot;
    private readonly Func<string> _logDirectory;

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

    public static string DefaultStagingRoot =>
        Path.Combine(FileSystem.AppDataDirectory, StagingDirectoryName);

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

        if (!IsSafeInstall())
        {
            return Result.Failure<string>(UpdateErrors.UnsafeInstallLocation);
        }

        string releaseDirectory = Path.Combine(_stagingRoot(), Sanitize(update.LatestVersion));

        PruneStagedReleases(releaseDirectory);

        try
        {
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

        string archivePath = Path.Combine(releaseDirectory, ArchiveFileName(update.AssetName));

        Result<string> download = await DownloadAsync(update.DownloadUrl, archivePath, progress, cancellationToken);
        if (download.IsFailure)
        {
            return Result.Failure<string>(download.Error);
        }

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

        string? payload = ResolvePayload(unpacked);
        if (payload is null)
        {
            _logger.LogWarning("The downloaded update did not contain a Helix build.");

            TryDeleteDirectory(releaseDirectory);

            return Result.Failure<string>(UpdateErrors.DownloadNotHelix);
        }

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

        if (!IsSafeInstall())
        {
            return Result.Failure(UpdateErrors.UnsafeInstallLocation);
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

                if (total is > 0)
                {
                    progress?.Report((double)copied / total.Value);
                }
            }

            return Convert.ToHexString(digest.GetHashAndReset());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
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

    private static string? ResolvePayload(string unpacked)
    {
        if (LooksLikeHelix(unpacked))
        {
            return unpacked;
        }

        foreach (string candidate in Directory.EnumerateDirectories(unpacked))
        {
            if (LooksLikeHelix(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

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

        return directory.EndsWith(".app", StringComparison.OrdinalIgnoreCase) &&
               Directory.Exists(Path.Combine(directory, "Contents", "MacOS"));
    }

    private bool IsSafeInstall()
    {
        string install = _installDirectory();

        if (!IsSafeToReplace(install))
        {
            _logger.LogWarning("Refusing to update: the install folder is not one that can be replaced wholesale.");

            return false;
        }

        if (ReleaseFolderOf(install) is not null)
        {
            _logger.LogWarning("Refusing to update: Helix is running from inside the update staging folder.");

            return false;
        }

        return true;
    }

    internal static bool IsSafeToReplace(string directory)
    {
        string install;

        try
        {
            install = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;
        }

        if (Path.GetDirectoryName(install) is null)
        {
            return false;
        }

#if !MACCATALYST
        if (!File.Exists(Path.Combine(install, WindowsExecutable)))
        {
            return false;
        }
#endif

        Environment.SpecialFolder[] shellFolders =
        [
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolder.Desktop,
            Environment.SpecialFolder.DesktopDirectory,
            Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.MyMusic,
            Environment.SpecialFolder.MyPictures,
            Environment.SpecialFolder.MyVideos,
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.CommonApplicationData,
            Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.System,
        ];

        foreach (Environment.SpecialFolder folder in shellFolders)
        {
            string path = Environment.GetFolderPath(folder);

            if (!string.IsNullOrEmpty(path) && SamePath(path, install))
            {
                return false;
            }
        }

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrEmpty(profile) && SamePath(Path.Combine(profile, "Downloads"), install))
        {
            return false;
        }

        return true;
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

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

    internal string WriteSwapScript(string stagedDirectory, string installDirectory)
    {
        int processId = Environment.ProcessId;

        string scriptDirectory = Path.Combine(_stagingRoot(), "helper");
        Directory.CreateDirectory(scriptDirectory);

        string logDirectory = _logDirectory();
        Directory.CreateDirectory(logDirectory);

        string logPath = Path.Combine(logDirectory, HelperLogFileName);

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

            $"if (Get-Process -Id {processId} -ErrorAction SilentlyContinue) {{",
            $"    Write-Log 'Helix was still running after {ExitWaitSeconds} seconds, so the update was not applied.'",
            "    exit",
            "}",

            "Start-Sleep -Seconds 2",
            "$moved = $false",
            "$reason = 'the folder was still held after every attempt'",
            "$stale = $false",
            "try {",
            "    if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force }",
            "} catch {",
            "    $stale = $true",
            "    Write-Log \"A previous backup at $backup could not be removed: $($_.Exception.Message). The update was not applied.\"",
            "}",
            "if (-not $stale) {",
            $"    foreach ($attempt in 1..{MoveAttempts}) {{",
            "        try {",
            "            Move-Item -LiteralPath $install -Destination $backup -Force",
            "            $moved = $true",
            "            break",
            "        } catch {",
            "            $reason = $_.Exception.Message",
            "            Start-Sleep -Seconds 1",
            "        }",
            "    }",
            "}",
            "if ($moved) {",
            "    try {",
            "        New-Item -ItemType Directory -Path $install -Force | Out-Null",
            "        Get-ChildItem -LiteralPath $staged -Force | Copy-Item -Destination $install -Recurse -Force",
            "        Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction SilentlyContinue",

            "        if ($release) { Remove-Item -LiteralPath $release -Recurse -Force -ErrorAction SilentlyContinue }",
            "        Write-Log 'The update was applied.'",
            "    } catch {",
            "        Write-Log \"The update could not be copied over the install: $($_.Exception.Message). The previous version was put back.\"",
            "        try {",
            "            if (Test-Path -LiteralPath $install) {",
            "                Remove-Item -LiteralPath $install -Recurse -Force -ErrorAction SilentlyContinue",
            "            }",

            "            if (Test-Path -LiteralPath $install) {",
            "                Get-ChildItem -LiteralPath $backup -Force | Copy-Item -Destination $install -Recurse -Force",
            "            } else {",
            "                Move-Item -LiteralPath $backup -Destination $install -Force",
            "            }",
            "        } catch {",
            "            Write-Log \"The previous version could not be put back either: $($_.Exception.Message). It is still at $backup.\"",
            "        }",
            "    }",
            "} elseif (-not $stale) {",
            "    Write-Log \"The install folder could not be moved aside, so the update was not applied and the previous version is still installed: $reason\"",
            "}",

            "if (Test-Path -LiteralPath $exe) { Start-Process -FilePath $exe -WorkingDirectory $install }",
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
            $"if kill -0 {processId} 2>/dev/null; then",
            $"  write_log 'Helix was still running after {ExitWaitSeconds} seconds, so the update was not applied.'",
            "  exit 0",
            "fi",
            "sleep 2",
            "if ! rm -rf \"$backup\"; then",
            "  write_log 'A previous backup could not be removed, so the update was not applied.'",
            "  open \"$install\"",
            "  exit 0",
            "fi",
            "if mv \"$install\" \"$backup\"; then",

            "  if ditto \"$staged\" \"$install\"; then",
            "    rm -rf \"$backup\"",
            "    [ -n \"$release\" ] && rm -rf \"$release\"",
            "    write_log 'The update was applied.'",
            "  else",
            "    write_log 'The update could not be copied over the install. The previous version was put back.'",
            "    rm -rf \"$install\"",
            "    if [ -e \"$install\" ]; then",
            "      ditto \"$backup\" \"$install\" || write_log 'The previous version could not be put back either; it is still beside the install as .old.'",
            "    else",
            "      mv \"$backup\" \"$install\" || write_log 'The previous version could not be put back either; it is still beside the install as .old.'",
            "    fi",
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

    private static string Escape(string path) =>
#if WINDOWS
        path.Replace("'", "''");
#else
        path.Replace("\"", "\\\"");
#endif

    private static string ArchiveFileName(string? assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName))
        {
            return "update.zip";
        }

        string name = Sanitize(Path.GetFileName(assetName));

        return string.IsNullOrWhiteSpace(name) ? "update.zip" : name;
    }

    private static string Sanitize(string version) =>
        string.Concat(version.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

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
