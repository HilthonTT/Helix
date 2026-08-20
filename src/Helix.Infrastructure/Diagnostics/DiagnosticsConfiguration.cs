namespace Helix.Infrastructure.Diagnostics;

internal static class DiagnosticsConfiguration
{
    /// <summary>Folder under the per-user app data directory that holds the log files.</summary>
    public const string LogDirectoryName = "logs";

    /// <summary>
    /// How long a log file is kept. Long enough to cover "it started doing this last
    /// week", short enough that an unattended install never accumulates without bound.
    /// </summary>
    public const int RetainedDays = 14;

    /// <summary>
    /// Beside the database, in the per-user app data directory, so a published folder
    /// can be replaced without taking the logs with it.
    /// </summary>
    public static string LogDirectory =>
        Path.Combine(FileSystem.AppDataDirectory, LogDirectoryName);
}
