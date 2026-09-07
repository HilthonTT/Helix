using Helix.Application.Abstractions.Diagnostics;
using System.IO.Compression;

namespace Helix.Infrastructure.Diagnostics;

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
        return Task.Run(() => Export(targetDirectory, cancellationToken), cancellationToken);
    }

    private Result<string> Export(string targetDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            return Result.Failure<string>(DiagnosticsErrors.InvalidTargetDirectory);
        }

        _writer.Flush();

        IReadOnlyList<string> files = _writer.GetFiles();
        if (files.Count == 0)
        {
            return Result.Failure<string>(DiagnosticsErrors.NoLogs);
        }

        string path = Path.Combine(targetDirectory, $"{ExportPrefix}{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip");

        bool created = false;

        try
        {
            var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write);

            created = true;

            using var archive = new ZipArchive(destination, ZipArchiveMode.Create);

            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using FileStream source = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using Stream entry = archive.CreateEntry(Path.GetFileName(file), CompressionLevel.Optimal).Open();

                source.CopyTo(entry);
            }

            return path;
        }
        catch (OperationCanceledException)
        {
            TryDeletePartialExport(path, created);

            return Result.Failure<string>(DiagnosticsErrors.ExportFailed("The export was cancelled."));
        }
        catch (UnauthorizedAccessException)
        {
            TryDeletePartialExport(path, created);

            return Result.Failure<string>(DiagnosticsErrors.ExportFailed(
                "Helix is not allowed to write to that folder."));
        }
        catch (Exception ex)
        {
            TryDeletePartialExport(path, created);

            return Result.Failure<string>(DiagnosticsErrors.ExportFailed(ex.Message));
        }
    }

    private static void TryDeletePartialExport(string path, bool created)
    {
        if (!created)
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
        }
    }
}
