namespace Helix.Application.Abstractions.Diagnostics;

public static class DiagnosticsErrors
{
    public static readonly Error NoLogs = Error.NotFound(
        "Diagnostics.NoLogs",
        "There are no log files to export yet.");

    public static readonly Error InvalidTargetDirectory = Error.Problem(
        "Diagnostics.InvalidTargetDirectory",
        "That folder path is not valid, please choose a different one.");

    public static readonly Error ExportCancelled = Error.Problem(
        "Diagnostics.ExportCancelled",
        "The diagnostics export was cancelled.");

    public static readonly Error ExportNotPermitted = Error.Problem(
        "Diagnostics.ExportNotPermitted",
        "The diagnostics could not be exported: Helix is not allowed to write to that folder.");

    public static Error ExportFailed(string message) => Error.Problem(
        "Diagnostics.ExportFailed",
        $"The diagnostics could not be exported: {message}",
        message);
}
