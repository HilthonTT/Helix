namespace Helix.Application.Abstractions.Diagnostics;

public interface IDiagnosticsLog
{
    string DirectoryPath { get; }

    Task<Result<string>> ExportAsync(string targetDirectory, CancellationToken cancellationToken = default);
}
