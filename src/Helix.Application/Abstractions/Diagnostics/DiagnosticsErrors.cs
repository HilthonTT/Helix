namespace Helix.Application.Abstractions.Diagnostics;

public static class DiagnosticsErrors
{
    public static readonly Error NoLogs = Error.NotFound(
        "Diagnostics.NoLogs",
        "There are no log files to export yet.");

    public static readonly Error InvalidTargetDirectory = Error.Problem(
        "Diagnostics.InvalidTargetDirectory",
        "That folder path is not valid, please choose a different one.");

    public static Error ExportFailed(string message) => Error.Problem(
        "Diagnostics.ExportFailed",
        $"The diagnostics could not be exported: {message}");
}
