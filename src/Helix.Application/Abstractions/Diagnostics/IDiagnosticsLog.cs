namespace Helix.Application.Abstractions.Diagnostics;

/// <summary>
/// The on-disk log, and the one thing a user ever needs to do with it: hand it over.
/// </summary>
/// <remarks>
/// Helix does most of its work unattended — watching shares, reconnecting them, retrying
/// on a backoff — and until this existed all of that was reported through
/// <c>Debug.WriteLine</c>, which no released build writes anywhere. A bug report that
/// said "it stopped reconnecting" left nothing at all to look at.
/// </remarks>
public interface IDiagnosticsLog
{
    /// <summary>Directory the log files are written to.</summary>
    string DirectoryPath { get; }

    /// <summary>
    /// Copies the current log files into a single zip inside <paramref name="targetDirectory"/>
    /// and returns its full path.
    /// </summary>
    /// <remarks>
    /// A copy rather than a move: the app is still running and still writing, and the
    /// user may well want to reproduce the fault again after sending this.
    /// </remarks>
    Task<Result<string>> ExportAsync(string targetDirectory, CancellationToken cancellationToken = default);
}
