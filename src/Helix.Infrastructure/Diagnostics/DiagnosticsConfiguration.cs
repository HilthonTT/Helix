namespace Helix.Infrastructure.Diagnostics;

internal static class DiagnosticsConfiguration
{
    public const string LogDirectoryName = "logs";

    public const int RetainedDays = 14;

    public static string LogDirectory =>
        Path.Combine(FileSystem.AppDataDirectory, LogDirectoryName);
}
