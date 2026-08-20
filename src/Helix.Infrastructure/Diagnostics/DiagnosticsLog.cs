using Helix.Application.Abstractions.Diagnostics;
using System.IO.Compression;

namespace Helix.Infrastructure.Diagnostics;

/// <summary>
/// Reads the log directory back out for the "export diagnostics" button.
/// </summary>
internal sealed class DiagnosticsLog : IDiagnosticsLog
{
    private const string ExportPrefix = "helix-diagnostics-";

    private readonly LogFileWriter _writer;

    public DiagnosticsLog(LogFileWriter writer)
    {
        _writer = writer;
    }

    public string DirectoryPath => _writer.DirectoryPath;

    public Task<Result<string>> ExportAsync(string targetDirectory, CancellationToken cancellationToken = default)
    {
        // Zipping is file I/O over what can be several megabytes; keep it off whichever
        // thread asked, which in practice is the UI one.
        return Task.Run(() => Export(targetDirectory, cancellationToken), cancellationToken);
    }

    private Result<string> Export(string targetDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            return Result.Failure<string>(DiagnosticsErrors.InvalidTargetDirectory);
        }

        // Anything still buffered belongs in the export — the last line before a fault is
        // usually the interesting one.
        _writer.Flush();

        IReadOnlyList<string> files = _writer.GetFiles();
        if (files.Count == 0)
        {
            return Result.Failure<string>(DiagnosticsErrors.NoLogs);
        }

        string path = Path.Combine(targetDirectory, $"{ExportPrefix}{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip");

        try
        {
            using var archive = new ZipArchive(
                new FileStream(path, FileMode.CreateNew, FileAccess.Write),
                ZipArchiveMode.Create);

            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Opened share-all: the running app still holds the current day's file
                // open for writing, and that is exactly the file worth having.
                using FileStream source = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using Stream entry = archive.CreateEntry(Path.GetFileName(file), CompressionLevel.Optimal).Open();

                source.CopyTo(entry);
            }

            return path;
        }
        catch (OperationCanceledException)
        {
            TryDeletePartialExport(path);

            return Result.Failure<string>(DiagnosticsErrors.ExportFailed("The export was cancelled."));
        }
        catch (UnauthorizedAccessException)
        {
            return Result.Failure<string>(DiagnosticsErrors.ExportFailed(
                "Helix is not allowed to write to that folder."));
        }
        catch (Exception ex)
        {
            TryDeletePartialExport(path);

            return Result.Failure<string>(DiagnosticsErrors.ExportFailed(ex.Message));
        }
    }

    /// <summary>
    /// Removes a zip that was only partly written, so the user is never handed a
    /// truncated archive that looks like a finished one.
    /// </summary>
    private static void TryDeletePartialExport(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // Already failing; a leftover file is the lesser problem.
        }
    }
}
