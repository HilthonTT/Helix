namespace Helix.Application.Abstractions.Diagnostics;

/// <summary>
/// Failures from the diagnostics export.
/// </summary>
/// <remarks>
/// Public and next to <see cref="IDiagnosticsLog"/> rather than in <c>Core/Errors</c>,
/// because the implementation lives in Infrastructure and returns these — the errors are
/// part of the abstraction's contract, not an internal detail of the Application layer.
/// </remarks>
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
